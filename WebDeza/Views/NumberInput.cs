using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace WebDeza.Views;

/// <summary>
/// 値を Enter かフォーカスが外れたときに当てはめる入力欄（編集パネルの数値・パラメーターの数値と色）の共通のキー操作。
/// 入力欄の Text のバインドは、既定の LostFocus で当てはめる
/// </summary>
public static class NumberInput
{
    /// <summary>Enter で入力した値を当てはめ、Esc で今の値に戻す（どちらも、続けて入力できるように全体を選んでおく）</summary>
    public static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        var binding = BindingOperations.GetBindingExpression(box, TextBox.TextProperty);
        switch (e.Key)
        {
            case Key.Enter:
                binding?.UpdateSource();
                break;
            case Key.Escape:
                binding?.UpdateTarget();
                break;
            default:
                return;
        }
        box.SelectAll();
        e.Handled = true;
    }

    /// <summary>キーボードでフォーカスが来たら、全体を選ぶ（打てば置き換わる）</summary>
    public static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) box.SelectAll();
    }
}
