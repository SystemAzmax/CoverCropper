using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WebDeza.ViewModels;

namespace WebDeza.Views.Dialogs;

/// <summary>
/// 保存のダイアログ（設計書 F-08・F-10・4.6）。保存できたら閉じる（DialogResult = true）。
/// 保存の処理をしている間は待機カーソルにし、閉じさせない。Enter で［保存］、Esc で［キャンセル］。
/// 大きさは、開くときに ViewModel の DialogWidth / DialogHeight（設定のもの）にし、閉じるときに今の大きさを書き戻す
/// </summary>
public partial class ExportDialog : Window
{
    private readonly ExportDialogViewModel _vm;

    public ExportDialog(ExportDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyRememberedSize();
        Closed += (_, _) =>
        {
            viewModel.OnClosed(); // 保存サイズの計算をやめる
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    private void OnCloseRequested(object? sender, EventArgs e) => DialogResult = true;

    /// <summary>覚えている大きさにする（画面の作業領域より大きければ収め、最小より小さくはしない）</summary>
    private void ApplyRememberedSize()
    {
        var area = SystemParameters.WorkArea;
        if (_vm.DialogWidth is { } width && double.IsFinite(width))
            Width = Math.Clamp(width, MinWidth, Math.Max(MinWidth, area.Width));
        if (_vm.DialogHeight is { } height && double.IsFinite(height))
            Height = Math.Clamp(height, MinHeight, Math.Max(MinHeight, area.Height));
    }

    // ---------------------------------------------------------------- プレビューのズーム（設計書 F-09）

    private void OnPreviewFitClick(object sender, RoutedEventArgs e) => PreviewCanvas.Zoom(ZoomAction.Fit);

    private void OnPreviewActualSizeClick(object sender, RoutedEventArgs e) => PreviewCanvas.Zoom(ZoomAction.ActualSize);

    private void OnPreviewZoomOutClick(object sender, RoutedEventArgs e) => PreviewCanvas.Zoom(ZoomAction.Out);

    private void OnPreviewZoomInClick(object sender, RoutedEventArgs e) => PreviewCanvas.Zoom(ZoomAction.In);

    /// <summary>上限（KB）の入力欄: Enter で当てはめ、Esc で今の値に戻す（編集パネルの数値の入力欄と同じ）</summary>
    private void OnNumberInputKeyDown(object sender, KeyEventArgs e) => NumberInput.OnKeyDown(sender, e);

    private void OnNumberInputGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => NumberInput.OnGotKeyboardFocus(sender, e);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExportDialogViewModel.IsSaving))
            Mouse.OverrideCursor = _vm.IsSaving ? Cursors.Wait : null;
    }

    /// <summary>保存の処理をしている間は閉じない（Esc・［×］）。閉じるときは、今の大きさを覚える</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_vm.IsSaving)
        {
            e.Cancel = true;
            return;
        }
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _vm.DialogWidth = Math.Round(bounds.Width);
            _vm.DialogHeight = Math.Round(bounds.Height);
        }
    }
}
