using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WebDeza.Views;

/// <summary>
/// キー入力の決まり（設計書 4.4・5.10-7）。メインウィンドウの PreviewKeyDown で、キーを道具に渡すかどうかを決める。
/// </summary>
/// <remarks>
/// 修飾キーの無い（または Shift だけの。Shift+矢印のため）キーは、アクティブな画像の今の道具に先に渡し、
/// 道具が使わなければ、道具の切り替え（M / H など）にする。ただし、入力欄・スライダー・コンボボックス・メニューに
/// フォーカスがあるときは、そのコントロールの操作を優先して渡さない。Ctrl・Alt つきのキーはメニューのキー（InputBindings）に任せる。
/// </remarks>
public static class KeyRouting
{
    /// <summary>道具（今の道具・道具の切り替え）にキーを渡すか</summary>
    /// <param name="key">押したキー</param>
    /// <param name="modifiers">修飾キー</param>
    /// <param name="inputControlFocused">入力欄・スライダー・コンボボックス・メニューにフォーカスがあるか</param>
    public static bool ShouldRouteToTools(Key key, ModifierKeys modifiers, bool inputControlFocused)
    {
        if (inputControlFocused) return false;
        if (modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return false;
        return key is not (Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
            or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.Tab);
    }

    /// <summary>道具の切り替えのキーとして使えるか（修飾キーなし）</summary>
    public static bool IsToolShortcut(ModifierKeys modifiers) => modifiers == ModifierKeys.None;

    /// <summary>
    /// フォーカスのある要素が、キーを自分で使うコントロール（入力欄・スライダー・コンボボックス・メニュー）か、その中にあるか
    /// </summary>
    public static bool IsInputControl(DependencyObject? focused)
    {
        for (var element = focused; element is not null; element = Parent(element))
        {
            if (element is TextBoxBase or PasswordBox or Slider or ComboBox or MenuBase or MenuItem)
                return true;
        }
        return false;
    }

    private static DependencyObject? Parent(DependencyObject element)
        => element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
}
