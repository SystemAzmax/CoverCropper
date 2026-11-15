using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WebDeza.ViewModels;

namespace WebDeza.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.ExitRequested += (_, _) => Close();
        _vm.ArrangeWindowsRequested += OnArrangeWindowsRequested;
        _vm.ZoomRequested += OnZoomRequested;
        WindowMenu.SubmenuClosed += OnWindowMenuClosed;
        RestorePlacement();
    }

    // ---------------------------------------------------------------- ドラッグ＆ドロップ（設計書 F-01）

    /// <summary>今のドラッグを受け入れるか（入ってきたときに 1 回だけ判定する。ドラッグ中にファイルを見に行かないため）</summary>
    private bool _dropAccepted;

    private static string[]? GetDroppedPaths(DragEventArgs e)
        => e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    private void OnPreviewDragEnter(object sender, DragEventArgs e)
    {
        _dropAccepted = _vm.CanAcceptDrop(GetDroppedPaths(e));
        UpdateDrag(e);
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e) => UpdateDrag(e);

    /// <summary>受け入れるものを含むときはハイライトして Copy、それ以外は None（読み込み中は受け入れない）</summary>
    private void UpdateDrag(DragEventArgs e)
    {
        bool accept = _dropAccepted && !_vm.IsBusy;
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropHighlight.Visibility = accept ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private async void OnPreviewDrop(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;
        e.Handled = true;

        var paths = GetDroppedPaths(e);
        if (!_vm.CanAcceptDrop(paths)) return;

        Activate();
        await _vm.OpenPathsAsync(paths!);
    }

    // ---------------------------------------------------------------- ズーム（メニュー［表示］・ツールバー）

    /// <summary>メニューとツールバーのズームは、アクティブな画像の子ウィンドウのキャンバスに伝える（設計書 5.6）</summary>
    private void OnZoomRequested(object? sender, ZoomAction action)
    {
        if (_vm.ActiveDocument is { } document && Workspace.GetWindow(document) is { } window)
            window.Canvas.Zoom(action);
    }

    // ---------------------------------------------------------------- メニュー［ウィンドウ］

    private void OnArrangeWindowsRequested(object? sender, WindowArrangement arrangement)
    {
        if (arrangement == WindowArrangement.Cascade) Workspace.Cascade();
        else Workspace.Tile();
    }

    /// <summary>メニュー［ウィンドウ］を開いたら、区切りの後に、開いている画像の一覧（開いた順。アクティブな画像に印）を作る</summary>
    private void OnWindowMenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, WindowMenu)) return;

        RemoveWindowListItems();
        WindowListSeparator.Visibility = _vm.Documents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _vm.Documents.Count; i++)
        {
            var document = _vm.Documents[i];
            // Fluent テーマは、IsCheckable の項目にだけチェックの欄を出す（選ぶとメニューは閉じ、次に開くときに作り直す）
            WindowMenu.Items.Add(new MenuItem
            {
                Header = WindowListHeader(i, document.FileName),
                IsCheckable = true,
                IsChecked = document.IsActive,
                FontWeight = document.IsActive ? FontWeights.Bold : FontWeights.Normal, // 印に加えて、太字でも見分けられるように
                Command = _vm.ActivateDocumentCommand,
                CommandParameter = document,
            });
        }
    }

    /// <summary>メニューを閉じたら一覧を外す（閉じた画像への参照を残さないため。設計書 5.10-11）</summary>
    private void OnWindowMenuClosed(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, WindowMenu)) RemoveWindowListItems();
    }

    private void RemoveWindowListItems()
    {
        int start = WindowMenu.Items.IndexOf(WindowListSeparator) + 1;
        while (WindowMenu.Items.Count > start) WindowMenu.Items.RemoveAt(start);
    }

    /// <summary>
    /// 一覧の項目の文字: 「1: photo.jpg」（数字で始まるファイル名でも番号と見分けられるように、コロンで区切る）。
    /// 1〜9 番目は、その数字がアクセスキーになる。
    /// ファイル名の「_」は、アクセスキーの印と見なされないように「__」にする
    /// </summary>
    /// <param name="index">開いた順の番号（0 から）</param>
    /// <param name="fileName">ファイル名</param>
    public static string WindowListHeader(int index, string fileName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentNullException.ThrowIfNull(fileName);
        int number = index + 1;
        string name = fileName.Replace("_", "__");
        return number <= 9 ? $"_{number}: {name}" : $"{number}: {name}";
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 読み込み中・書き出し中は待機カーソル（設計書 4.2）
        if (e.PropertyName == nameof(MainViewModel.IsBusy))
            Mouse.OverrideCursor = _vm.IsBusy ? Cursors.Wait : null;
    }

    // ---------------------------------------------------------------- ウィンドウの位置の保存・復元

    private void RestorePlacement()
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (WindowPlacement.GetRestoreBounds(_vm.Settings, screen, MinWidth, MinHeight) is not { } bounds)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (_vm.Settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        var normal = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        WindowPlacement.Save(_vm.Settings, WindowState, normal);
        _vm.SaveSettingsQuietly();
    }
}
