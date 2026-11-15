namespace WebDeza.Imaging.Editing;

/// <summary>
/// 切り抜きの枠の計算。すべて純粋関数で、座標は編集画像（回転・反転を適用したフル解像度の画像）のピクセル座標（整数）。
/// </summary>
public static class CropFrameCalculator
{
    /// <summary>枠の幅・高さの最小（画像がこれより小さいときは画像の大きさ）</summary>
    public const int MinFrameLength = 16;

    /// <summary>書き出しサイズが「幅×高さ」のときの拡大の上限（枠の幅 ≥ ceil(書き出し幅 ÷ これ)）</summary>
    public const int MaxUpscale = 8;

    /// <summary>
    /// 枠の規則を作る。
    /// 比率は、書き出しサイズが「幅×高さ」ならその縦横比（比率の選択は無視）、そうでなければ比率の選択に従う。
    /// </summary>
    /// <param name="imageWidth">編集画像の幅</param>
    /// <param name="imageHeight">編集画像の高さ</param>
    /// <param name="aspect">比率の選択</param>
    /// <param name="outputSize">書き出しサイズ</param>
    public static FrameRules Rules(int imageWidth, int imageHeight, CropAspect aspect, OutputSize outputSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentNullException.ThrowIfNull(outputSize);

        bool exact = outputSize.Mode == OutputSizeMode.Exact;
        if (exact && (outputSize.Width <= 0 || outputSize.Height <= 0))
            throw new ArgumentException("書き出しサイズの幅と高さは 1 以上にしてください。", nameof(outputSize));

        AspectRatio? ratio = exact
            ? new AspectRatio(outputSize.Width, outputSize.Height)
            : aspect.Kind switch
            {
                CropAspectKind.Free => null,
                CropAspectKind.Original => new AspectRatio(imageWidth, imageHeight),
                _ => aspect.Ratio,
            };

        // 比率が自由: 最大は画像全体、最小は 16×16（画像のほうが小さければ画像の大きさ）
        if (ratio is not { } a)
        {
            return new FrameRules(imageWidth, imageHeight, null,
                Math.Min(MinFrameLength, imageWidth), Math.Min(MinFrameLength, imageHeight),
                imageWidth, imageHeight);
        }

        // 最大の枠: 画像に収まる、比率に合ういちばん大きい枠
        int maxWidth = (long)imageWidth * a.Height >= (long)imageHeight * a.Width
            ? Math.Clamp(RoundDiv((long)imageHeight * a.Width, a.Height), 1, imageWidth)
            : imageWidth;
        var partial = new FrameRules(imageWidth, imageHeight, a, 0, 0, maxWidth, 0);
        int maxHeight = partial.HeightForWidth(maxWidth);

        // 最小の枠: 幅・高さとも 16 以上。「幅×高さ」のときは、拡大が 8 倍までになるよう幅を広げる
        long minWidth = Math.Max(MinFrameLength, CeilDiv((long)MinFrameLength * a.Width, a.Height));
        if (exact) minWidth = Math.Max(minWidth, CeilDiv(outputSize.Width, MaxUpscale));
        if (minWidth >= maxWidth)
            return partial with { MinWidth = maxWidth, MinHeight = maxHeight, MaxHeight = maxHeight };

        return partial with
        {
            MinWidth = (int)minWidth,
            MinHeight = partial.HeightForWidth((int)minWidth),
            MaxHeight = maxHeight,
        };
    }

    /// <summary>初期状態の枠: 最大の枠を画像の中央に置く（端数は切り捨て）。自由のときは画像全体</summary>
    public static PixelRect Initial(FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return Centered(rules.MaxWidth, rules.MaxHeight, rules);
    }

    /// <summary>
    /// 枠を規則に合わせる。幅を最小〜最大に収め、比率が決まっていれば高さを幅から求め、
    /// 自由なら高さも最小〜最大に収める。そのうえで位置を画像の内側に収める
    /// </summary>
    public static PixelRect Clamp(PixelRect rect, FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var (width, height) = ClampSize(rect.Width, rect.Height, rules);
        return new PixelRect(
            Math.Clamp(rect.X, 0, rules.ImageWidth - width),
            Math.Clamp(rect.Y, 0, rules.ImageHeight - height),
            width, height);
    }

    /// <summary>枠の大きさを保って（規則に合わせたうえで）、画像の中央に置く（端数は切り捨て）</summary>
    public static PixelRect Center(PixelRect rect, FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var (width, height) = ClampSize(rect.Width, rect.Height, rules);
        return Centered(width, height, rules);
    }

    /// <summary>
    /// 枠から書き出す画像の大きさ。
    /// 幅×高さ: 指定の大きさ。長辺: 長辺が指定の長さになるよう、縮尺 s = 長辺 ÷ max(枠の幅, 枠の高さ) を掛けて丸める（最低 1）。
    /// 指定しない: 枠の大きさのまま
    /// </summary>
    public static (int Width, int Height) OutputPixelSize(PixelRect rect, OutputSize outputSize)
    {
        ArgumentNullException.ThrowIfNull(outputSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rect.Width, nameof(rect));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rect.Height, nameof(rect));

        switch (outputSize.Mode)
        {
            case OutputSizeMode.Exact:
                return (outputSize.Width, outputSize.Height);

            case OutputSizeMode.LongSide:
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputSize.LongSide, nameof(outputSize));
                int longest = Math.Max(rect.Width, rect.Height);
                return (Math.Max(1, RoundDiv((long)rect.Width * outputSize.LongSide, longest)),
                        Math.Max(1, RoundDiv((long)rect.Height * outputSize.LongSide, longest)));
            }

            case OutputSizeMode.Original:
                return (rect.Width, rect.Height);

            default:
                throw new ArgumentOutOfRangeException(nameof(outputSize), outputSize.Mode, null);
        }
    }

    // ---------------------------------------------------------------- 内部

    private static (int Width, int Height) ClampSize(int width, int height, FrameRules rules)
    {
        int w = Math.Clamp(width, rules.MinWidth, rules.MaxWidth);
        int h = rules.Aspect is null
            ? Math.Clamp(height, rules.MinHeight, rules.MaxHeight)
            : rules.HeightForWidth(w);
        return (w, h);
    }

    private static PixelRect Centered(int width, int height, FrameRules rules)
        => new((rules.ImageWidth - width) / 2, (rules.ImageHeight - height) / 2, width, height);

    /// <summary>numerator ÷ denominator を四捨五入（0.5 は 0 から遠い方へ）した整数。どちらも 0 以上・分母は正とする</summary>
    internal static int RoundDiv(long numerator, long denominator)
        => (int)Math.Min(int.MaxValue, (2 * numerator + denominator) / (2 * denominator));

    /// <summary>numerator ÷ denominator の切り上げ。どちらも 0 以上・分母は正とする</summary>
    private static long CeilDiv(long numerator, long denominator) => (numerator + denominator - 1) / denominator;
}
