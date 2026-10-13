using System.Windows.Media;
using CoverCropper.Services;

namespace CoverCropper.Tests;

public class ImageResizerTests
{
    private readonly ImageResizer _resizer = new();

    [Theory]
    [InlineData(3000, 4000, 1024, 1365)] // U01 縦長
    [InlineData(4000, 3000, 1024, 768)]  // U02 4:3
    [InlineData(6000, 2000, 2304, 768)]  // U03 横長
    [InlineData(800, 600, 1024, 768)]    // U04 小さい画像（拡大）
    [InlineData(500, 100, 3840, 768)]    // U05 拡大
    public void CalculateSize_MatchesSpecExamples(int w, int h, int expectedW, int expectedH)
    {
        Assert.Equal((expectedW, expectedH), _resizer.CalculateSize(w, h));
    }

    [Fact] // U06
    public void CalculateSize_HeightJustBelow768_UsesHeightBase()
    {
        var (w, h) = _resizer.CalculateSize(1024, 767);
        Assert.Equal(768, h);
        Assert.True(w >= 1025, $"width={w}");
    }

    [Fact] // U07
    public void CalculateSize_RandomSizes_AlwaysContainsCropFrame()
    {
        var random = new Random(12345);
        for (int i = 0; i < 1000; i++)
        {
            int srcW = random.Next(1, 20000);
            int srcH = random.Next(1, 20000);
            var (w, h) = _resizer.CalculateSize(srcW, srcH);
            Assert.True(w >= AppConstants.TargetWidth && h >= AppConstants.TargetHeight,
                $"{srcW}x{srcH} -> {w}x{h}");
        }
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void CalculateSize_InvalidSize_Throws(int w, int h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _resizer.CalculateSize(w, h));
    }

    [Theory]
    [InlineData(4000, 3000)] // 縮小
    [InlineData(800, 600)]   // 拡大
    [InlineData(1024, 768)]  // 等倍
    public void Resize_ProducesRequestedPixelSizeAt96Dpi(int srcW, int srcH)
    {
        var source = TestImages.CreateGradient(srcW, srcH, dpi: 300);
        var (w, h) = _resizer.CalculateSize(srcW, srcH);

        var result = _resizer.Resize(source, w, h);

        Assert.Equal(w, result.PixelWidth);
        Assert.Equal(h, result.PixelHeight);
        Assert.Equal(96, result.DpiX);
        Assert.Equal(96, result.DpiY);
        Assert.Equal(PixelFormats.Bgr24, result.Format);
        Assert.True(result.IsFrozen);
    }

    [Fact]
    public void Resize_SolidColor_KeepsColor()
    {
        var source = TestImages.CreateSolid(3000, 2000, Color.FromRgb(10, 128, 250), PixelFormats.Bgr24);

        var result = _resizer.Resize(source, 1152, 768);

        foreach (var (x, y) in new[] { (0, 0), (576, 384), (1151, 767) })
        {
            var px = TestImages.GetPixel(result, x, y);
            Assert.Equal(new byte[] { 250, 128, 10 }, px);
        }
    }

    [Fact]
    public void Resize_SameSize_IsIdentity()
    {
        var source = TestImages.CreateGradient(1024, 768);

        var result = _resizer.Resize(source, 1024, 768);

        foreach (var (x, y) in new[] { (0, 0), (100, 200), (1023, 767) })
            Assert.Equal(TestImages.GetPixel(source, x, y), TestImages.GetPixel(result, x, y));
    }
}
