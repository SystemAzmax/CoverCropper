using System.Windows;

namespace WebDeza.Views.Canvas;

/// <summary>
/// キャンバスの表示倍率と位置（設計書 F-03・5.6）。画面を使わずにテストできるよう、値だけを持つ。
/// </summary>
/// <param name="Scale">画像の 1 px あたりの DIP（100% は 1 ÷ dpiScale）</param>
/// <param name="OffsetX">画像の左上を表示する位置（DIP。キャンバスの左上が原点）</param>
/// <param name="OffsetY">画像の左上を表示する位置（DIP）</param>
/// <remarks>
/// 画面の座標（DIP）と画像のピクセル座標を行き来する。ズームとパンの状態は表示だけの関心事なので、
/// ViewModel には持たせず、キャンバス（子ウィンドウごと）が持つ（設計書 5.6）。
/// </remarks>
public readonly record struct ViewTransform(double Scale, double OffsetX, double OffsetY)
{
    /// <summary>
    /// 全体表示: 画像全体が、周りに margin を空けた内側に収まる倍率で、中央に置く。
    /// ただし 100%（画像の 1 px = 1 物理ピクセル）を超えて拡大しない（設計書 F-03）。
    /// 位置は物理ピクセルの境目にそろえる（にじませないため）
    /// </summary>
    /// <param name="viewport">キャンバスの大きさ（DIP）</param>
    /// <param name="imageWidth">画像の幅（px）</param>
    /// <param name="imageHeight">画像の高さ（px）</param>
    /// <param name="dpiScale">1 DIP あたりの物理ピクセル（100% の DPI なら 1）</param>
    /// <param name="margin">画像の周りに空ける余白（DIP）</param>
    public static ViewTransform Fit(Size viewport, int imageWidth, int imageHeight, double dpiScale, double margin)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiScale);

        double availableWidth = Math.Max(1, viewport.Width - margin * 2);
        double availableHeight = Math.Max(1, viewport.Height - margin * 2);
        double scale = Math.Min(1 / dpiScale, Math.Min(availableWidth / imageWidth, availableHeight / imageHeight));

        double x = Math.Round((viewport.Width - imageWidth * scale) / 2 * dpiScale) / dpiScale;
        double y = Math.Round((viewport.Height - imageHeight * scale) / 2 * dpiScale) / dpiScale;
        return new ViewTransform(scale, x, y);
    }

    /// <summary>
    /// viewPoint（画面の点）を中心に、倍率を factor 倍にする。倍率は [minScale, maxScale] に収める。
    /// その点の下にある画像の点は、ズームの前後で変わらない
    /// </summary>
    public ViewTransform ZoomAt(Point viewPoint, double factor, double minScale, double maxScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minScale);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxScale, minScale);

        double scale = Math.Clamp(Scale * factor, minScale, maxScale);
        var image = ViewToImage(viewPoint);
        return new ViewTransform(scale, viewPoint.X - image.X * scale, viewPoint.Y - image.Y * scale);
    }

    /// <summary>表示位置を delta（DIP）だけ動かす（パン）。範囲に収めるのは ClampPan</summary>
    public ViewTransform Pan(Vector delta) => this with { OffsetX = OffsetX + delta.X, OffsetY = OffsetY + delta.Y };

    /// <summary>
    /// パンの範囲に収める（設計書 F-03）。画像が領域より小さい向きは中央に固定し、
    /// 大きい向きは、画像の端が領域の内側に入り込まない範囲にする
    /// </summary>
    public ViewTransform ClampPan(Size viewport, int imageWidth, int imageHeight)
        => this with
        {
            OffsetX = ClampAxis(OffsetX, viewport.Width, imageWidth * Scale),
            OffsetY = ClampAxis(OffsetY, viewport.Height, imageHeight * Scale),
        };

    private static double ClampAxis(double offset, double viewport, double displayed)
        => displayed <= viewport
            ? (viewport - displayed) / 2
            : Math.Clamp(offset, viewport - displayed, 0);

    /// <summary>ズームの 1 段階の倍率（設計書 F-03）</summary>
    public const double ZoomStep = 1.25;

    /// <summary>
    /// ズームできる倍率の範囲（設計書 F-03）: 最小は「全体表示の倍率」と 10% の小さい方、最大は 3200%（% は物理ピクセルで数える）
    /// </summary>
    /// <param name="fitScale">全体表示の倍率（Scale）</param>
    /// <param name="dpiScale">1 DIP あたりの物理ピクセル</param>
    public static (double Min, double Max) ScaleRange(double fitScale, double dpiScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fitScale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiScale);
        return (Math.Min(fitScale, 0.1 / dpiScale), 32 / dpiScale);
    }

    /// <summary>
    /// キャンバスの大きさが変わったとき（ズーム中）: 倍率と、表示の中心にある画像の点を保つ（設計書 F-03）
    /// </summary>
    public ViewTransform Resized(Size oldViewport, Size newViewport)
    {
        var center = ViewToImage(new Point(oldViewport.Width / 2, oldViewport.Height / 2));
        return this with
        {
            OffsetX = newViewport.Width / 2 - center.X * Scale,
            OffsetY = newViewport.Height / 2 - center.Y * Scale,
        };
    }

    /// <summary>位置を物理ピクセルの境目にそろえる（描くときに、画像をにじませないため）。倍率は変えない</summary>
    public ViewTransform SnapToPixels(double dpiScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiScale);
        return this with
        {
            OffsetX = Math.Round(OffsetX * dpiScale) / dpiScale,
            OffsetY = Math.Round(OffsetY * dpiScale) / dpiScale,
        };
    }

    /// <summary>画像のピクセル座標を、画面の座標（DIP）にする</summary>
    public Point ImageToView(double x, double y) => new(OffsetX + x * Scale, OffsetY + y * Scale);

    /// <summary>画面の座標（DIP）を、画像のピクセル座標（実数）にする</summary>
    public Point ViewToImage(Point viewPoint) => new((viewPoint.X - OffsetX) / Scale, (viewPoint.Y - OffsetY) / Scale);

    /// <summary>画像全体を描く矩形（DIP）</summary>
    public Rect ImageBounds(int imageWidth, int imageHeight)
        => new(OffsetX, OffsetY, imageWidth * Scale, imageHeight * Scale);

    /// <summary>表示倍率（%）。100% = 画像の 1 px が 1 物理ピクセル（設計書 F-03）</summary>
    public double ZoomPercent(double dpiScale) => Scale * dpiScale * 100;
}
