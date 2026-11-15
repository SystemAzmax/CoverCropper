using System.Windows;
using WebDeza.Views.Canvas;

namespace WebDeza.Tests.Views;

/// <summary>表示倍率と位置の計算（設計書 F-03・5.6。V24・V25。実装計画書 8-1。全体表示は 5-5 の EditorCanvas.Fit から移した）</summary>
public class ViewTransformTests
{
    private const double Margin = 12;

    // ---------------------------------------------------------------- V24: 全体表示

    [Fact]
    public void Fit_LargeImage_FitsInsideMarginAndIsCentered()
    {
        // 余白 12 を除いた 976×576 に、4000×2000 を収める → 幅で決まり 0.244
        var view = ViewTransform.Fit(new Size(1000, 600), 4000, 2000, dpiScale: 1, Margin);

        Assert.Equal(976.0 / 4000, view.Scale, 10);
        Assert.Equal((12.0, 56.0), (view.OffsetX, view.OffsetY));
        var bounds = view.ImageBounds(4000, 2000);
        Assert.Equal(976, bounds.Width, 6);
        Assert.Equal(488, bounds.Height, 6);
    }

    [Fact]
    public void Fit_TallImage_LimitedByHeight()
    {
        var view = ViewTransform.Fit(new Size(1000, 600), 1200, 1600, dpiScale: 1, Margin);

        Assert.Equal(576.0 / 1600, view.Scale, 10);
        Assert.Equal(12, view.OffsetY);
    }

    [Fact]
    public void Fit_SmallImage_IsNotEnlargedBeyond100PercentAndCentered()
    {
        var view = ViewTransform.Fit(new Size(1000, 600), 300, 200, dpiScale: 1, Margin);

        Assert.Equal(new ViewTransform(1, 350, 200), view);
        Assert.Equal(100, view.ZoomPercent(1), 9);
    }

    [Fact]
    public void Fit_SmallImageAtHighDpi_Is100PercentAndCentered()
    {
        // V24: 150% の環境では、100% = 1 px が 1/1.5 DIP。300×200 は 200×133.3 DIP で、中央に置く
        var view = ViewTransform.Fit(new Size(1000, 600), 300, 200, dpiScale: 1.5, Margin);

        Assert.Equal(1 / 1.5, view.Scale, 10);
        Assert.Equal(100, view.ZoomPercent(1.5), 9);
        var bounds = view.ImageBounds(300, 200);
        Assert.Equal(1000 / 2.0, bounds.X + bounds.Width / 2, 0.5); // 物理ピクセルにそろえる分だけずれてよい
        Assert.Equal(600 / 2.0, bounds.Y + bounds.Height / 2, 0.5);
    }

    [Fact]
    public void Fit_LargeImageAtHighDpi_FitsAndScaleIsAtMost100Percent()
    {
        // V24: 大きい画像は収まり、Scale は 1 ÷ 1.5 以下
        var view = ViewTransform.Fit(new Size(1000, 600), 6000, 4000, dpiScale: 1.5, Margin);

        Assert.True(view.Scale <= 1 / 1.5);
        Assert.Equal(576.0 / 4000, view.Scale, 10); // 縦で決まる
        var bounds = view.ImageBounds(6000, 4000);
        const double eps = 1e-6;
        Assert.True(bounds.Left >= Margin - eps && bounds.Top >= Margin - eps, bounds.ToString());
        Assert.True(bounds.Right <= 1000 - Margin + eps && bounds.Bottom <= 600 - Margin + eps, bounds.ToString());
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void Fit_PositionIsAlignedToPhysicalPixels(double dpiScale)
    {
        var view = ViewTransform.Fit(new Size(1001.3, 600.7), 333, 211, dpiScale, Margin);

        Assert.Equal(Math.Round(view.OffsetX * dpiScale), view.OffsetX * dpiScale, 9);
        Assert.Equal(Math.Round(view.OffsetY * dpiScale), view.OffsetY * dpiScale, 9);
    }

    [Fact]
    public void Fit_TinyViewport_DoesNotFail()
    {
        var view = ViewTransform.Fit(new Size(0, 0), 100, 100, dpiScale: 1, Margin);

        Assert.True(view.Scale > 0);
        Assert.True(view.ImageBounds(100, 100).Width > 0);
    }

    // ---------------------------------------------------------------- 座標の行き来

    [Fact]
    public void ImageToViewAndBack_AreInverse()
    {
        var view = new ViewTransform(0.37, 15.5, -220.25);

        var viewPoint = view.ImageToView(1234, 567);
        var back = view.ViewToImage(viewPoint);

        Assert.Equal(15.5 + 1234 * 0.37, viewPoint.X, 9);
        Assert.Equal(-220.25 + 567 * 0.37, viewPoint.Y, 9);
        Assert.Equal(1234, back.X, 9);
        Assert.Equal(567, back.Y, 9);
    }

    [Theory]
    [InlineData(1.0, 0.5, 50)]
    [InlineData(1.5, 1 / 1.5, 100)]
    [InlineData(2.0, 16.0, 3200)]
    public void ZoomPercent_IsPhysicalPixelsPerImagePixel(double dpiScale, double scale, double expected)
    {
        Assert.Equal(expected, new ViewTransform(scale, 0, 0).ZoomPercent(dpiScale), 9);
    }

    // ---------------------------------------------------------------- V25: ズーム

    [Theory]
    [InlineData(1.25)]
    [InlineData(0.8)]
    [InlineData(4.0)]
    public void ZoomAt_PointUnderCursorStays(double factor)
    {
        var view = new ViewTransform(0.25, 40, 30);
        var cursor = new Point(333.3, 177.7);
        var before = view.ViewToImage(cursor);

        var zoomed = view.ZoomAt(cursor, factor, minScale: 0.01, maxScale: 32);
        var after = zoomed.ViewToImage(cursor);

        Assert.Equal(0.25 * factor, zoomed.Scale, 10);
        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
    }

    [Fact]
    public void ZoomAt_ScaleIsClampedAndPointStillStays()
    {
        var view = new ViewTransform(30, -1000, -500);
        var cursor = new Point(200, 100);
        var before = view.ViewToImage(cursor);

        var max = view.ZoomAt(cursor, 1.25, minScale: 0.1, maxScale: 32);
        var min = new ViewTransform(0.12, 10, 10).ZoomAt(cursor, 0.5, minScale: 0.1, maxScale: 32);

        Assert.Equal(32, max.Scale);
        Assert.Equal(before.X, max.ViewToImage(cursor).X, 9);
        Assert.Equal(before.Y, max.ViewToImage(cursor).Y, 9);
        Assert.Equal(0.1, min.Scale);
    }

    [Fact]
    public void ZoomAt_RepeatedInAndOut_ReturnsToSameView()
    {
        var view = new ViewTransform(0.5, 12, 34);
        var cursor = new Point(250, 125);

        var result = view;
        for (int i = 0; i < 5; i++) result = result.ZoomAt(cursor, 1.25, 0.01, 32);
        for (int i = 0; i < 5; i++) result = result.ZoomAt(cursor, 1 / 1.25, 0.01, 32);

        Assert.Equal(view.Scale, result.Scale, 9);
        Assert.Equal(view.OffsetX, result.OffsetX, 6);
        Assert.Equal(view.OffsetY, result.OffsetY, 6);
    }

    // ---------------------------------------------------------------- V25: パンの範囲

    [Theory]
    [InlineData(5000, 5000)]     // 右下へ大きくパン
    [InlineData(-50000, -50000)] // 左上へ大きくパン
    [InlineData(-300, -200)]     // 範囲の中
    public void ClampPan_LargerThanViewport_EdgesDoNotComeInside(double offsetX, double offsetY)
    {
        // 4000×3000 を 0.5 倍 = 2000×1500 DIP を、1000×600 の領域に表示する
        var viewport = new Size(1000, 600);
        var clamped = new ViewTransform(0.5, offsetX, offsetY).ClampPan(viewport, 4000, 3000);
        var bounds = clamped.ImageBounds(4000, 3000);

        Assert.True(bounds.Left <= 0 && bounds.Top <= 0, bounds.ToString());
        Assert.True(bounds.Right >= viewport.Width && bounds.Bottom >= viewport.Height, bounds.ToString());
        if (offsetX == -300) Assert.Equal((-300.0, -200.0), (clamped.OffsetX, clamped.OffsetY)); // 範囲の中なら変えない
        Assert.Equal(0.5, clamped.Scale);
    }

    [Fact]
    public void ClampPan_SmallerDirection_IsCentered()
    {
        // 横は 2000 DIP（領域より大きい）、縦は 300 DIP（領域より小さい）→ 縦は中央に固定、横はパンできる
        var clamped = new ViewTransform(0.5, -400, -999).ClampPan(new Size(1000, 600), 4000, 600);

        Assert.Equal(-400, clamped.OffsetX);
        Assert.Equal(150, clamped.OffsetY);
    }

    [Fact]
    public void ClampPan_WholeImageSmaller_IsCenteredBothWays()
    {
        var clamped = new ViewTransform(1, 777, -777).ClampPan(new Size(1000, 600), 300, 200);

        Assert.Equal((350.0, 200.0), (clamped.OffsetX, clamped.OffsetY));
    }

    // ---------------------------------------------------------------- 8-2: 倍率の範囲・大きさの変化・ピクセルへのそろえ

    [Theory]
    [InlineData(0.05, 1.0, 0.05, 32.0)]       // 全体表示が 10% より小さい → 最小は全体表示の倍率
    [InlineData(0.5, 1.0, 0.1, 32.0)]         // 全体表示が 10% より大きい → 最小は 10%
    [InlineData(0.5, 1.5, 0.1 / 1.5, 32 / 1.5)] // 高 DPI では、% を物理ピクセルで数える
    public void ScaleRange_MinIsSmallerOfFitAnd10Percent_MaxIs3200Percent(double fitScale, double dpiScale, double min, double max)
    {
        var range = ViewTransform.ScaleRange(fitScale, dpiScale);

        Assert.Equal(min, range.Min, 10);
        Assert.Equal(max, range.Max, 10);
        Assert.Equal(3200, new ViewTransform(range.Max, 0, 0).ZoomPercent(dpiScale), 9);
    }

    [Fact]
    public void Resized_KeepsScaleAndImagePointAtCenter()
    {
        var view = new ViewTransform(2, -1500, -800);
        var oldSize = new Size(1000, 600);
        var newSize = new Size(700, 900);
        var centerBefore = view.ViewToImage(new Point(500, 300));

        var resized = view.Resized(oldSize, newSize);

        Assert.Equal(2, resized.Scale);
        var centerAfter = resized.ViewToImage(new Point(350, 450));
        Assert.Equal(centerBefore.X, centerAfter.X, 9);
        Assert.Equal(centerBefore.Y, centerAfter.Y, 9);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void SnapToPixels_OffsetsAreOnPhysicalPixelsAndScaleIsKept(double dpiScale)
    {
        var view = new ViewTransform(1.2345, 10.37, -33.61);

        var snapped = view.SnapToPixels(dpiScale);

        Assert.Equal(view.Scale, snapped.Scale);
        Assert.Equal(Math.Round(snapped.OffsetX * dpiScale), snapped.OffsetX * dpiScale, 9);
        Assert.Equal(Math.Round(snapped.OffsetY * dpiScale), snapped.OffsetY * dpiScale, 9);
        Assert.True(Math.Abs(snapped.OffsetX - view.OffsetX) <= 0.5 / dpiScale + 1e-9);
        Assert.True(Math.Abs(snapped.OffsetY - view.OffsetY) <= 0.5 / dpiScale + 1e-9);
    }

    [Fact]
    public void ZoomFromFit_StepsOf125Percent_Reach100AndStayInRange()
    {
        // 全体表示 25% から 1.25 倍ずつ拡大すると、上限の 3200% で止まる
        var viewport = new Size(1000, 600);
        var view = ViewTransform.Fit(viewport, 3800, 2200, dpiScale: 1, Margin);
        var (min, max) = ViewTransform.ScaleRange(view.Scale, 1);
        var center = new Point(500, 300);
        for (int i = 0; i < 40; i++)
            view = view.ZoomAt(center, ViewTransform.ZoomStep, min, max).ClampPan(viewport, 3800, 2200);

        Assert.Equal(3200, view.ZoomPercent(1), 9);
        var bounds = view.ImageBounds(3800, 2200);
        Assert.True(bounds.Left <= 0 && bounds.Right >= 1000 && bounds.Top <= 0 && bounds.Bottom >= 600);
    }
}
