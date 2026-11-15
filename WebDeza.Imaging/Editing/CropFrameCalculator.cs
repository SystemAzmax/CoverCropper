namespace WebDeza.Imaging.Editing;

/// <summary>
/// 矩形（選択範囲）の計算。すべて純粋関数で、座標は画像のピクセル座標（整数）（設計書 5.3）。
/// </summary>
public static class CropFrameCalculator
{
    /// <summary>
    /// 選択範囲の規則を作る（設計書 F-04）。
    /// 最大は、比率が無ければ画像全体、あれば画像に収まる比率どおりのいちばん大きい矩形。
    /// 最小は、幅・高さとも minLength 以上（比率があるときは、幅を ceil(minLength × 比率の幅 ÷ 比率の高さ) 以上にして、
    /// 高さも minLength 以上にする）。最小が最大より大きくなる場合は、最大に合わせる。
    /// </summary>
    /// <param name="imageWidth">画像の幅</param>
    /// <param name="imageHeight">画像の高さ</param>
    /// <param name="aspect">比率。null か default（0:0）なら比率なし（自由）</param>
    /// <param name="minLength">幅・高さの最小（既定 1）</param>
    public static FrameRules Rules(int imageWidth, int imageHeight, AspectRatio? aspect, int minLength = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minLength);

        // 比率が自由（null か default）: 最大は画像全体、最小は minLength 角（画像のほうが小さければ画像の大きさ）
        if (aspect is not { Width: > 0, Height: > 0 } a)
        {
            return new FrameRules(imageWidth, imageHeight, null,
                Math.Min(minLength, imageWidth), Math.Min(minLength, imageHeight),
                imageWidth, imageHeight);
        }

        // 最大: 画像に収まる、比率に合ういちばん大きい矩形
        int maxWidth = (long)imageWidth * a.Height >= (long)imageHeight * a.Width
            ? Math.Clamp(RoundDiv((long)imageHeight * a.Width, a.Height), 1, imageWidth)
            : imageWidth;
        var partial = new FrameRules(imageWidth, imageHeight, a, 0, 0, maxWidth, 0);
        int maxHeight = partial.HeightForWidth(maxWidth);

        // 最小: 幅・高さとも minLength 以上
        long minWidth = Math.Max(minLength, CeilDiv((long)minLength * a.Width, a.Height));
        if (minWidth >= maxWidth)
            return partial with { MinWidth = maxWidth, MinHeight = maxHeight, MaxHeight = maxHeight };

        return partial with
        {
            MinWidth = (int)minWidth,
            MinHeight = partial.HeightForWidth((int)minWidth),
            MaxHeight = maxHeight,
        };
    }

    /// <summary>最大の矩形を画像の中央に置く（端数は切り捨て）。比率が自由のときは画像全体（すべて選択・比率を変えたとき）</summary>
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

    // ---------------------------------------------------------------- 矩形の操作
    /// <summary>
    /// 矩形を px 単位で動かす（選択範囲の移動）。大きさは規則に合わせたまま変えず、画像の外には出ない
    /// </summary>
    /// <param name="rect">今の矩形</param>
    /// <param name="dx">横の移動量（px。右が正）</param>
    /// <param name="dy">縦の移動量（px。下が正）</param>
    /// <param name="rules">規則</param>
    public static PixelRect Move(PixelRect rect, int dx, int dy, FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rect = Clamp(rect, rules);
        // X + dx は int の範囲を超えうるので、long で計算してから収める
        return rect with
        {
            X = (int)Math.Clamp((long)rect.X + dx, 0, rules.ImageWidth - rect.Width),
            Y = (int)Math.Clamp((long)rect.Y + dy, 0, rules.ImageHeight - rect.Height),
        };
    }

    /// <summary>
    /// ドラッグの始点と終点を対角にする矩形（選択範囲を作る）。始点の角を固定し、終点の向きへ広げる。
    /// 画像の外へドラッグしたら端で止まり、比率があるときは比率に合わせる（幅と高さのうち、大きくなる方に合わせる）。
    /// 大きさは規則の最小〜最大に収める（始点が画像の端に近くて最小の大きさが入らないときは、内側へずらす）
    /// </summary>
    /// <param name="startX">始点（画像のピクセル座標。小数でよい。画素の境目の座標で、0〜画像の幅）</param>
    /// <param name="startY">始点</param>
    /// <param name="endX">終点（画像の外でもよい）</param>
    /// <param name="endY">終点</param>
    /// <param name="rules">規則</param>
    public static PixelRect FromDrag(double startX, double startY, double endX, double endY, FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!double.IsFinite(startX) || !double.IsFinite(startY) || !double.IsFinite(endX) || !double.IsFinite(endY))
            throw new ArgumentOutOfRangeException(nameof(startX), "ドラッグの位置が数値ではありません。");

        // 始点は画素の境目に合わせ、画像の内側に収める
        int sx = (int)Math.Clamp(Math.Round(startX, MidpointRounding.AwayFromZero), 0, rules.ImageWidth);
        int sy = (int)Math.Clamp(Math.Round(startY, MidpointRounding.AwayFromZero), 0, rules.ImageHeight);
        bool toRight = endX >= sx;
        bool toBottom = endY >= sy;

        // 始点の角に、最小の大きさの矩形を置き、反対側の角のハンドルを終点までドラッグしたものとして求める
        var seed = Clamp(new PixelRect(
            toRight ? sx : sx - rules.MinWidth,
            toBottom ? sy : sy - rules.MinHeight,
            rules.MinWidth, rules.MinHeight), rules);
        var handle = (toRight, toBottom) switch
        {
            (true, true) => FrameHandle.BottomRight,
            (true, false) => FrameHandle.TopRight,
            (false, true) => FrameHandle.BottomLeft,
            _ => FrameHandle.TopLeft,
        };
        return ResizeFromHandle(seed, handle, endX, endY, rules);
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
    /// 比率を変えたときの合わせ直し。中心と面積をなるべく保って、規則の比率に合わせる
    /// （面積を保つので、縦横を入れ替えたときは幅と高さが入れ替わり、何度入れ替えても縮まない）。
    /// 大きさは最小〜最大に収め（収まらなければ小さくする）、画像からはみ出す場合は内側へずらす。
    /// 比率が自由なら Clamp と同じ（今の枠のまま）
    /// </summary>
    /// <param name="rect">今の枠</param>
    /// <param name="rules">新しい比率の規則</param>
    public static PixelRect FitToAspect(PixelRect rect, FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Aspect is not { } a) return Clamp(rect, rules);

        double area = (double)Math.Max(1, rect.Width) * Math.Max(1, rect.Height);
        int width = Math.Clamp(RoundToInt(Math.Sqrt(area * a.Width / a.Height)), rules.MinWidth, rules.MaxWidth);
        int height = rules.HeightForWidth(width);

        // 中心を保つ（端数は左上へ寄せる）。はみ出した分は Clamp が内側へずらす
        long x = rect.X + (((long)rect.Width - width) >> 1);
        long y = rect.Y + (((long)rect.Height - height) >> 1);
        return Clamp(new PixelRect(
            (int)Math.Clamp(x, int.MinValue, int.MaxValue), (int)Math.Clamp(y, int.MinValue, int.MaxValue), width, height), rules);
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
