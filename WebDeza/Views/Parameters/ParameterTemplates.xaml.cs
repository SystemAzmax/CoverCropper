using System.Windows;
using System.Windows.Input;
using WebDeza.ViewModels;

namespace WebDeza.Views.Parameters;

/// <summary>パラメーターの入力欄のテンプレート（設計書 5.6）。スライダーのダブルクリックと、入力欄のキー操作を受ける</summary>
public partial class ParameterTemplates : ResourceDictionary
{
    public ParameterTemplates() => InitializeComponent();

    /// <summary>スライダーを左ボタンでダブルクリックしたら、既定値に戻す（設計書 F-06）</summary>
    private void OnSliderMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is FrameworkElement { DataContext: ParameterViewModel parameter })
        {
            parameter.ResetToDefault();
            e.Handled = true;
        }
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e) => NumberInput.OnKeyDown(sender, e);

    private void OnInputGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => NumberInput.OnGotKeyboardFocus(sender, e);
}
