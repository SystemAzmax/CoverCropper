namespace WebDeza.Imaging.Editing;

/// <summary>切り抜きの比率の選び方</summary>
public enum CropAspectKind
{
    /// <summary>自由</summary>
    Free,

    /// <summary>元画像と同じ</summary>
    Original,

    /// <summary>指定の比率（1:1・4:3 など）</summary>
    Fixed,
}

/// <summary>
/// 切り抜きの比率の選択（全画像共通）。書き出しサイズが「幅×高さ」のときは、これは使われず書き出しの縦横比になる。
/// </summary>
/// <param name="Kind">選び方</param>
/// <param name="Ratio">比率（Fixed のときだけ。それ以外は default にそろえる）</param>
public sealed record CropAspect(CropAspectKind Kind, AspectRatio Ratio = default)
{
    /// <summary>自由（既定）</summary>
    public static CropAspect Free { get; } = new(CropAspectKind.Free);

    /// <summary>元画像と同じ</summary>
    public static CropAspect Original { get; } = new(CropAspectKind.Original);

    /// <summary>指定の比率。Fixed のときは比率が要り、それ以外では比率を持たない（比較がぶれないようにする）</summary>
    public AspectRatio Ratio { get; init; } = Kind switch
    {
        CropAspectKind.Fixed when Ratio == default
            => throw new ArgumentException("比率を指定するときは、比率（Ratio）が要ります。", nameof(Ratio)),
        CropAspectKind.Fixed => Ratio,
        CropAspectKind.Free or CropAspectKind.Original => default,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
    };

    /// <summary>指定の比率（幅:高さ。約分して持つ）</summary>
    public static CropAspect Fixed(int width, int height) => new(CropAspectKind.Fixed, new AspectRatio(width, height));

    /// <summary>縦横を入れ替えたもの（4:3 → 3:4）。Fixed 以外はそのまま</summary>
    public CropAspect Swapped()
        => Kind == CropAspectKind.Fixed ? Fixed(Ratio.Height, Ratio.Width) : this;
}
