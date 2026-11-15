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

    /// <summary>書き出し倍率（書き出し画像の幅 ÷ 枠の幅）。1 を超えると拡大になる（M03）</summary>
    public static double OutputScale(PixelRect rect, OutputSize outputSize)
        => (double)OutputPixelSize(rect, outputSize).Width / rect.Width;

    // ---------------------------------------------------------------- 枠の操作

    /// <summary>
    /// 枠を書き出し画像の px 単位で動かす（矢印キー）。編集画像のピクセルでは、
    /// 1 回の移動量 n を max(1, round(|n| ÷ 書き出し倍率)) にする（縦も横と同じ倍率を使う）。画像の外には出ない
    /// </summary>
    /// <param name="rect">今の枠</param>
    /// <param name="outputDx">横の移動量（書き出し画像の px。右が正）</param>
    /// <param name="outputDy">縦の移動量（書き出し画像の px。下が正）</param>
    /// <param name="rules">枠の規則</param>
    /// <param name="outputSize">書き出しサイズ（書き出し倍率を求めるのに使う）</param>
    public static PixelRect Move(PixelRect rect, int outputDx, int outputDy, FrameRules rules, OutputSize outputSize)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rect = Clamp(rect, rules);
        int outputWidth = OutputPixelSize(rect, outputSize).Width;

        int ToImagePixels(int n)
        {
            if (n == 0) return 0;
            int pixels = Math.Max(1, RoundDiv(Math.Abs((long)n) * rect.Width, outputWidth));
            return n > 0 ? pixels : -pixels;
        }

        return Clamp(rect with { X = rect.X + ToImagePixels(outputDx), Y = rect.Y + ToImagePixels(outputDy) }, rules);
    }

    /// <summary>
    /// 枠の中心を保って幅を変える（＋ / － キー）。比率が決まっていれば高さは幅から求め、
    /// 自由なら今の枠の縦横比を保つ。大きさは最小〜最大に収め、画像からはみ出す場合は内側へずらす
    /// </summary>
    public static PixelRect ResizeAroundCenter(PixelRect rect, int newWidth, FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rect = Clamp(rect, rules);

        int width = Math.Clamp(newWidth, rules.MinWidth, rules.MaxWidth);
        int height = rules.Aspect is null
            ? Math.Clamp(RoundDiv((long)width * rect.Height, rect.Width), rules.MinHeight, rules.MaxHeight)
            : rules.HeightForWidth(width);

        // 中心を保つ（端数は左上へ寄せる）。はみ出した分は Clamp が内側へずらす
        int x = rect.X + FloorHalf(rect.Width - width);
        int y = rect.Y + FloorHalf(rect.Height - height);
        return Clamp(new PixelRect(x, y, width, height), rules);
    }

    /// <summary>
    /// ハンドルのドラッグで枠の大きさを変える。反対側の角（辺）を固定し、ポインターの位置に合わせる。
    /// 反対側を越えても裏返らず最小で止まり、画像の端でも止まる。
    /// 比率が決まっているときは、角のハンドルでは幅と高さのうち大きくなる方に合わせ、
    /// 辺のハンドルでは、もう一方の向きの中心を保つ
    /// </summary>
    /// <param name="rect">ドラッグを始めたときの枠</param>
    /// <param name="handle">ドラッグしているハンドル</param>
    /// <param name="pointerX">ポインターの位置（編集画像のピクセル座標。小数でよい）</param>
    /// <param name="pointerY">ポインターの位置（編集画像のピクセル座標。小数でよい）</param>
    /// <param name="rules">枠の規則</param>
    public static PixelRect ResizeFromHandle(PixelRect rect, FrameHandle handle, double pointerX, double pointerY, FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!double.IsFinite(pointerX) || !double.IsFinite(pointerY))
            throw new ArgumentOutOfRangeException(nameof(pointerX), "ポインターの位置が数値ではありません。");
        rect = Clamp(rect, rules);

        var (sx, sy) = Directions(handle);
        // 固定する側（反対側の辺）の座標と、動かす向きに使える長さ（画像の端まで）
        int anchorX = sx < 0 ? rect.X + rect.Width : rect.X;
        int anchorY = sy < 0 ? rect.Y + rect.Height : rect.Y;
        int roomX = sx < 0 ? anchorX : rules.ImageWidth - anchorX;
        int roomY = sy < 0 ? anchorY : rules.ImageHeight - anchorY;
        // ポインターまでの長さ（反対側を越えたときは負になり、最小で止まる）
        double wantWidth = sx < 0 ? anchorX - pointerX : pointerX - anchorX;
        double wantHeight = sy < 0 ? anchorY - pointerY : pointerY - anchorY;

        int width, height;
        if (rules.Aspect is not { } a)
        {
            width = sx == 0 ? rect.Width : Math.Clamp(RoundToInt(wantWidth), rules.MinWidth, Math.Min(rules.MaxWidth, roomX));
            height = sy == 0 ? rect.Height : Math.Clamp(RoundToInt(wantHeight), rules.MinHeight, Math.Min(rules.MaxHeight, roomY));
        }
        else
        {
            // 比率が決まっているとき: 幅で決め、高さは幅から求める
            double widthFromHeight = wantHeight * a.Width / a.Height;
            double want = (sx, sy) switch
            {
                (0, _) => widthFromHeight,                      // 上・下の辺
                (_, 0) => wantWidth,                            // 左・右の辺
                _ => Math.Max(wantWidth, widthFromHeight),      // 角: 大きくなる方に合わせる
            };

            // 動かす向きに収まる幅（辺のハンドルでは、もう一方の向きは画像全体まで使える）
            long limit = rules.MaxWidth;
            if (sx != 0) limit = Math.Min(limit, roomX);
            // 高さ round(幅 × a.H ÷ a.W) が roomY 以下になる、いちばん大きい幅
            if (sy != 0) limit = Math.Min(limit, ((2L * roomY + 1) * a.Width - 1) / (2L * a.Height));
            width = (int)Math.Clamp(RoundToInt(want), rules.MinWidth, Math.Max(rules.MinWidth, limit));
            height = rules.HeightForWidth(width);
        }

        // 固定した側から位置を決める。辺のハンドルで動かさない向きは、中心を保つ
        int x = sx switch
        {
            < 0 => anchorX - width,
            > 0 => anchorX,
            _ => rect.X + FloorHalf(rect.Width - width),
        };
        int y = sy switch
        {
            < 0 => anchorY - height,
            > 0 => anchorY,
            _ => rect.Y + FloorHalf(rect.Height - height),
        };
        return Clamp(new PixelRect(x, y, width, height), rules);
    }

    /// <summary>
    /// 画像を反転したときに、枠の位置を反転に合わせて移す（大きさは保つ）。
    /// 左右反転: X' = 画像の幅 − X − 枠の幅、上下反転: Y' = 画像の高さ − Y − 枠の高さ
    /// </summary>
    /// <param name="rect">反転する前の枠</param>
    /// <param name="horizontal">true なら左右反転、false なら上下反転</param>
    /// <param name="rules">反転した後の画像の枠の規則（反転では画像の大きさは変わらない）</param>
    public static PixelRect Mirror(PixelRect rect, bool horizontal, FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rect = Clamp(rect, rules);
        return horizontal
            ? rect with { X = rules.ImageWidth - rect.X - rect.Width }
            : rect with { Y = rules.ImageHeight - rect.Y - rect.Height };
    }

    // ---------------------------------------------------------------- 内部

    /// <summary>ハンドルが動かす向き（−1: 左・上の辺を動かす、+1: 右・下の辺を動かす、0: その向きは動かさない）</summary>
    private static (int X, int Y) Directions(FrameHandle handle) => handle switch
    {
        FrameHandle.TopLeft => (-1, -1),
        FrameHandle.Top => (0, -1),
        FrameHandle.TopRight => (1, -1),
        FrameHandle.Right => (1, 0),
        FrameHandle.BottomRight => (1, 1),
        FrameHandle.Bottom => (0, 1),
        FrameHandle.BottomLeft => (-1, 1),
        FrameHandle.Left => (-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, null),
    };

    /// <summary>四捨五入（0.5 は 0 から遠い方へ）して int に収める</summary>
    private static int RoundToInt(double value)
        => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);

    /// <summary>value ÷ 2 の切り捨て（負の数も小さい方へ）</summary>
    private static int FloorHalf(int value) => value >> 1;

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
