using CommunityToolkit.Mvvm.ComponentModel;
using WebDeza.Views.Canvas;

namespace WebDeza.ViewModels;

/// <summary>道具のボタン 1 つ（登録内容から作る。設計書 4.2）</summary>
public sealed partial class ToolItemViewModel : ObservableObject
{
    public ToolItemViewModel(ICanvasToolFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = factory;
    }

    /// <summary>道具の種類</summary>
    public ICanvasToolFactory Factory { get; }

    /// <summary>道具の名前（例: 手のひら）</summary>
    public string DisplayName => Factory.DisplayName;

    /// <summary>ボタンに出す文字（道具のキー。例: H）</summary>
    public string ShortcutText => Factory.Shortcut.ToString();

    /// <summary>ツールチップ（例: 手のひら (H)）</summary>
    public string ToolTip => $"{DisplayName} ({ShortcutText})";

    /// <summary>今の道具か</summary>
    [ObservableProperty]
    private bool _isSelected;
}
