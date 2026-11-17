using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WebDeza.ViewModels;

namespace WebDeza.Views.Dialogs;

/// <summary>
/// 保存のダイアログ（設計書 F-08・F-10・4.6）。保存できたら閉じる（DialogResult = true）。
/// 保存の処理をしている間は待機カーソルにし、閉じさせない。Enter で［保存］、Esc で［キャンセル］
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
        Closed += (_, _) =>
        {
            viewModel.OnClosed(); // 保存サイズの計算をやめる
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    private void OnCloseRequested(object? sender, EventArgs e) => DialogResult = true;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExportDialogViewModel.IsSaving))
            Mouse.OverrideCursor = _vm.IsSaving ? Cursors.Wait : null;
    }

    /// <summary>保存の処理をしている間は閉じない（Esc・［×］）</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_vm.IsSaving) e.Cancel = true;
    }
}
