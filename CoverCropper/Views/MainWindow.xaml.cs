using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CoverCropper.ViewModels;

namespace CoverCropper.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        RestorePlacement();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsBusy))
            Mouse.OverrideCursor = _vm.IsBusy ? Cursors.Wait : null;
    }

    // ---------------------------------------------------------------- ドラッグ＆ドロップ

    private static string[]? GetDroppedPaths(DragEventArgs e)
        => e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        bool accept = _vm.CanAcceptDrop(GetDroppedPaths(e));
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
        if (paths is not { Length: > 0 }) return;

        Activate();
        await _vm.DropAsync(paths);
    }

    // ---------------------------------------------------------------- キー操作

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_vm.HasImage || _vm.IsBusy) return;

        var modifiers = Keyboard.Modifiers;
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;

        int step = modifiers.HasFlag(ModifierKeys.Shift) ? AppConstants.KeyMoveStepLarge : AppConstants.KeyMoveStep;
        switch (e.Key)
        {
            case Key.Left: _vm.Move(-step, 0); break;
            case Key.Right: _vm.Move(step, 0); break;
            case Key.Up: _vm.Move(0, -step); break;
            case Key.Down: _vm.Move(0, step); break;
            case Key.Home when modifiers == ModifierKeys.None:
                _vm.ResetPositionCommand.Execute(null);
                break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- ウィンドウ位置の保存・復元

    private void RestorePlacement()
    {
        var s = _vm.Settings;
        if (s.WindowWidth is not { } w || s.WindowHeight is not { } h || s.WindowLeft is not { } left || s.WindowTop is not { } top)
            return;

        w = Math.Max(MinWidth, w);
        h = Math.Max(MinHeight, h);

        // 画面外（モニター構成変更など）に出てしまう場合は既定位置のままにする
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(new Rect(left, top, w, h)) || !screen.Contains(new Point(left + 50, top + 10)))
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
        Width = w;
        Height = h;
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var s = _vm.Settings;
        if (!bounds.IsEmpty)
        {
            s.WindowLeft = bounds.Left;
            s.WindowTop = bounds.Top;
            s.WindowWidth = bounds.Width;
            s.WindowHeight = bounds.Height;
        }
        s.WindowMaximized = WindowState == WindowState.Maximized;
        _vm.SaveSettingsQuietly();
    }
}
