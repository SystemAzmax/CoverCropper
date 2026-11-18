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

        // 圧縮前と圧縮後のキャンバスは、表示（倍率と位置）を共有して、ズーム・パンを連動させる（設計書 F-09）
        RenderedCanvas.Viewport = PreviewCanvas.Viewport;
        UpdatePreviewLayout();
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

    /// <summary>「並べて比較」なら左右に同じ幅ずつ（間は 8 DIP）、「結果のみ」なら圧縮後だけを広げる</summary>
    private void UpdatePreviewLayout()
    {
        bool side = _vm.IsSideBySide;
        RenderedColumn.Width = side ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PreviewGapColumn.Width = new GridLength(side ? 8 : 0);
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
        // 1 枚の保存の間は待機カーソル。一括保存の間は［中止］を押せるよう、待機カーソルにしない（プログレスバーを出す）
        if (e.PropertyName == nameof(ExportDialogViewModel.IsSaving))
            Mouse.OverrideCursor = _vm.IsSaving && !_vm.IsBatchRunning ? Cursors.Wait : null;
        else if (e.PropertyName == nameof(ExportDialogViewModel.IsSideBySide))
            UpdatePreviewLayout();
    }

    /// <summary>一括保存の実行中の Esc は［中止］（設計書 4.4・F-11）</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && _vm.IsBatchRunning)
        {
            _vm.CancelBatchCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 保存の処理をしている間は閉じない（Esc・［×］）。一括保存の実行中に閉じようとしたら［中止］にする。閉じるときは、今の大きさを覚える
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_vm.IsSaving)
        {
            if (_vm.IsBatchRunning) _vm.CancelBatchCommand.Execute(null);
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
