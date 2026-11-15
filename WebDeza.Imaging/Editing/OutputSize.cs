namespace WebDeza.Imaging.Editing;

/// <summary>書き出す大きさの決め方</summary>
public enum OutputSizeMode
{
    /// <summary>幅×高さ（切り抜きの比率はこの縦横比に固定される）</summary>
    Exact,

    /// <summary>長辺（枠の長辺がこの長さになるよう拡大・縮小する）</summary>
    LongSide,

    /// <summary>指定しない（切り抜いた大きさのまま）</summary>
    Original,
}

/// <summary>
/// 書き出す大きさ。3 つの方式の値を全部持つので、方式を切り替えてもそれぞれの値を覚えておける。
/// 使われるのは Mode に合う値だけ（Exact: Width と Height、LongSide: LongSide）。
/// </summary>
/// <param name="Mode">方式</param>
/// <param name="Width">幅（Exact のとき）</param>
/// <param name="Height">高さ（Exact のとき）</param>
/// <param name="LongSide">長辺（LongSide のとき）</param>
public sealed record OutputSize(OutputSizeMode Mode, int Width, int Height, int LongSide)
{
    /// <summary>幅・高さ・長辺に指定できる最小</summary>
    public const int MinLength = 16;

    /// <summary>幅・高さ・長辺に指定できる最大</summary>
    public const int MaxLength = 8192;

    /// <summary>長辺の既定値</summary>
    public const int DefaultLongSide = 1920;

    /// <summary>幅×高さ（長辺は既定値を覚えておく）</summary>
    public static OutputSize ExactSize(int width, int height) => new(OutputSizeMode.Exact, width, height, DefaultLongSide);

    /// <summary>長辺（幅×高さは表紙画像と同じ 1024×768 を覚えておく）</summary>
    public static OutputSize LongSideOf(int longSide) => new(OutputSizeMode.LongSide, 1024, 768, longSide);

    /// <summary>指定しない（幅×高さと長辺は既定値を覚えておく）</summary>
    public static OutputSize OriginalSize { get; } = new(OutputSizeMode.Original, 1024, 768, DefaultLongSide);

    /// <summary>Mode に合う値が MinLength〜MaxLength の範囲にあるか</summary>
    public bool IsValid => Mode switch
    {
        OutputSizeMode.Exact => InRange(Width) && InRange(Height),
        OutputSizeMode.LongSide => InRange(LongSide),
        OutputSizeMode.Original => true,
        _ => false,
    };

    /// <summary>書き出しの縦横比（Exact のときだけ。それ以外は null）</summary>
    public AspectRatio? ExactAspect => Mode == OutputSizeMode.Exact && Width > 0 && Height > 0
        ? new AspectRatio(Width, Height)
        : null;

    private static bool InRange(int length) => length is >= MinLength and <= MaxLength;
}
