using CommunityToolkit.Mvvm.ComponentModel;

namespace WebDeza.ViewModels;

/// <summary>選択範囲の比率の選び方</summary>
public enum AspectChoice
{
    /// <summary>自由</summary>
    Free,

    /// <summary>元画像と同じ</summary>
    Original,

    /// <summary>決まった比率（1:1・4:3・3:2・16:9）</summary>
    Preset,

    /// <summary>カスタム（幅:高さを入力する）</summary>
    Custom,
}

/// <summary>
/// 編集パネルの「選択範囲」の比率の選択欄の項目 1 つ（設計書 F-04）。
/// 決まった比率は横長（幅 ≧ 高さ）で持ち、［縦横を入れ替え］で縦長にしているときは、表示も「3:4」のように入れ替える
/// </summary>
public sealed partial class AspectOptionViewModel : ObservableObject
{
    private AspectOptionViewModel(AspectChoice choice, string label, int width = 0, int height = 0)
    {
        Choice = choice;
        Width = width;
        Height = height;
        _label = label;
    }

    /// <summary>選び方</summary>
    public AspectChoice Choice { get; }

    /// <summary>決まった比率の幅（横長のとき）</summary>
    public int Width { get; }

    /// <summary>決まった比率の高さ（横長のとき）</summary>
    public int Height { get; }

    /// <summary>選択欄に出す文字</summary>
    [ObservableProperty]
    private string _label;

    /// <summary>縦長にしているか（決まった比率のとき）に合わせて、表示を変える</summary>
    internal void UpdateLabel(bool portrait)
    {
        if (Choice == AspectChoice.Preset) Label = portrait ? $"{Height}:{Width}" : $"{Width}:{Height}";
    }

    /// <summary>縦長のときの幅と高さ（決まった比率のとき）</summary>
    internal (int Width, int Height) Oriented(bool portrait) => portrait ? (Height, Width) : (Width, Height);

    /// <summary>選択欄の項目（設計書 F-04 の順: 自由 / 元画像と同じ / 1:1 / 4:3 / 3:2 / 16:9 / カスタム）</summary>
    public static IReadOnlyList<AspectOptionViewModel> CreateAll() =>
    [
        new(AspectChoice.Free, "自由"),
        new(AspectChoice.Original, "元画像と同じ"),
        new(AspectChoice.Preset, "1:1", 1, 1),
        new(AspectChoice.Preset, "4:3", 4, 3),
        new(AspectChoice.Preset, "3:2", 3, 2),
        new(AspectChoice.Preset, "16:9", 16, 9),
        new(AspectChoice.Custom, "カスタム"),
    ];
}
