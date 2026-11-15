using System.Windows;
using System.Windows.Media;
using WebDeza.Imaging;
using WebDeza.Views.Canvas;

namespace WebDeza.Tests.Views;

/// <summary>キャンバスの選択範囲の位置と補間の計算（実装計画書 5-5・6-5。全体表示の計算は 8-1 で ViewTransformTests に移した）</summary>
public class EditorCanvasTests
{
    // ---------------------------------------------------------------- 選択範囲の位置（6-5）

    [Fact]
    public void ImageRectToView_ScalesFromImageRect()
    {
        // 6000×2000 を 0.1 倍で (12, 56) に描いているとき
        var rect = EditorCanvas.ImageRectToView(new PixelRect(1666, 0, 2667, 2000), new ViewTransform(0.1, 12, 56), dpiScale: 1);

        Assert.Equal(new Rect(179, 56, 266, 200), rect); // 左 178.6 → 179、右 445.3 → 445
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void ImageRectToView_EdgesAreAlignedToPhysicalPixels(double dpiScale)
    {
        var rect = EditorCanvas.ImageRectToView(new PixelRect(333, 77, 1111, 555), new ViewTransform(0.137, 12.4, 30.8), dpiScale);

        foreach (var value in new[] { rect.Left, rect.Top, rect.Right, rect.Bottom })
            Assert.Equal(Math.Round(value * dpiScale), value * dpiScale, 9);
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
