using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WebDeza.ViewModels;

namespace WebDeza.Views.Dialogs;

/// <summary>
/// 画像サイズのダイアログ（設計書 F-05・4.6）。開いたら幅の入力欄を選んだ状態にする。
/// Enter で OK（入力が正しいときだけ）、Esc でキャンセル。
/// </summary>
public partial class ImageSizeDialog : Window
{
    public ImageSizeDialog(ImageSizeDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private ImageSizeDialogViewModel ViewModel => (ImageSizeDialogViewModel)DataContext;

    /// <summary>
    /// 幅の入力欄にフォーカスを置き、全体を選ぶ（打てば置き換わる）。
    /// Loaded の時点で選んでも、ウィンドウが出来上がる途中で選択が外れるので、入力を受け付ける段階まで遅らせる
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WidthInput.Focus();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => WidthInput.SelectAll());
    }

    /// <summary>キーボードでフォーカスが来たら、全体を選ぶ（打てば置き換わる）</summary>
    private void OnInputGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) box.SelectAll();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanAccept) DialogResult = true;
    }
}
