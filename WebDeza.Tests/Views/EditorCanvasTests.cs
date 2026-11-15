using System.Windows;
using System.Windows.Media;
using WebDeza.Views.Canvas;

namespace WebDeza.Tests.Views;

/// <summary>キャンバスの全体表示と補間の計算（実装計画書 5-5。6-1 で ViewTransform に移す）</summary>
public class EditorCanvasTests
{
    [Fact]
    public void Fit_LargeImage_FitsInsideMarginAndIsCentered()
    {
        // 余白 12 を除いた 976×576 に、4000×2000 を収める → 幅で決まり 0.244
        var (rect, scale) = EditorCanvas.Fit(new Size(1000, 600), 4000, 2000, dpiScale: 1);

        Assert.Equal(976.0 / 4000, scale, 10);
        Assert.Equal(976, rect.Width, 6);
        Assert.Equal(488, rect.Height, 6);
        Assert.Equal((12.0, 56.0), (rect.X, rect.Y));
    }

    [Fact]
    public void Fit_TallImage_LimitedByHeight()
    {
        var (rect, scale) = EditorCanvas.Fit(new Size(1000, 600), 1200, 1600, dpiScale: 1);

        Assert.Equal(576.0 / 1600, scale, 10);
        Assert.Equal(576, rect.Height, 6);
        Assert.Equal(12, rect.Y);
    }

    [Fact]
    public void Fit_SmallImage_IsNotEnlargedBeyond100Percent()
    {
        var (rect, scale) = EditorCanvas.Fit(new Size(1000, 600), 300, 200, dpiScale: 1);

        Assert.Equal(1, scale);
        Assert.Equal(new Rect(350, 200, 300, 200), rect);
    }

    [Fact]
    public void Fit_HighDpi_100PercentIsOnePhysicalPixel()
    {
        // 150% の環境では、1 px = 1/1.5 DIP までしか拡大しない
        var (rect, scale) = EditorCanvas.Fit(new Size(1000, 600), 300, 200, dpiScale: 1.5);

        Assert.Equal(1 / 1.5, scale, 10);
        Assert.Equal(200, rect.Width, 6);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void Fit_PositionIsAlignedToPhysicalPixels(double dpiScale)
    {
        var (rect, _) = EditorCanvas.Fit(new Size(1001.3, 600.7), 333, 211, dpiScale);

        Assert.Equal(Math.Round(rect.X * dpiScale), rect.X * dpiScale, 9);
        Assert.Equal(Math.Round(rect.Y * dpiScale), rect.Y * dpiScale, 9);
    }

    [Fact]
    public void Fit_TinyViewport_DoesNotFail()
    {
        var (rect, scale) = EditorCanvas.Fit(new Size(0, 0), 100, 100, dpiScale: 1);

        Assert.True(scale > 0);
        Assert.True(rect.Width > 0);
    }

    [Theory]
    [InlineData(4096, 4096, 1.0, BitmapScalingMode.HighQuality)]        // 1 px = 1 物理ピクセル
    [InlineData(8191, 4096, 1.0, BitmapScalingMode.HighQuality)]        // 2 未満
    [InlineData(8192, 4096, 1.0, BitmapScalingMode.NearestNeighbor)]    // ちょうど 2
    [InlineData(5462, 4096, 1.5, BitmapScalingMode.NearestNeighbor)]    // 高 DPI では物理ピクセルで数える
    [InlineData(400, 4096, 2.0, BitmapScalingMode.HighQuality)]         // 縮小表示
    public void GetScalingMode_SwitchesAtTwoPhysicalPixels(double displayedWidth, int bitmapWidth, double dpiScale,
        BitmapScalingMode expected)
    {
        Assert.Equal(expected, EditorCanvas.GetScalingMode(displayedWidth, bitmapWidth, dpiScale));
    }
}
