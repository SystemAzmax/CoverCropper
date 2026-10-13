using System.Windows.Media;
using CoverCropper.Services;

namespace CoverCropper.Tests;

public class ImageCropperTests
{
    private readonly ImageCropper _cropper = new();

    [Theory] // U08
    [InlineData(-50, -10, 2304, 768, 0, 0)]
    [InlineData(5000, 5000, 2304, 768, 1280, 0)]
    [InlineData(5000, 5000, 1024, 1365, 0, 597)]
    [InlineData(-1, 9999, 1024, 768, 0, 0)]
    [InlineData(300, 0, 2304, 768, 300, 0)]
    public void Clamp_KeepsFrameInsideImage(int x, int y, int w, int h, int expectedX, int expectedY)
    {
        Assert.Equal((expectedX, expectedY), _cropper.Clamp(x, y, w, h));
    }

    [Theory]
    [InlineData(2304, 768, 640, 0)]  // U09
    [InlineData(1024, 1365, 0, 298)]
    [InlineData(1024, 768, 0, 0)]
    public void Center_ReturnsCenteredPosition(int w, int h, int expectedX, int expectedY)
    {
        Assert.Equal((expectedX, expectedY), _cropper.Center(w, h));
    }

    [Theory] // U10
    [InlineData(0, 0)]
    [InlineData(640, 0)]
    [InlineData(1280, 0)]
    [InlineData(99999, -5)]
    public void Crop_Returns1024x768(int x, int y)
    {
        var source = TestImages.CreateGradient(2304, 768);

        var result = _cropper.Crop(source, x, y);

        Assert.Equal(1024, result.PixelWidth);
        Assert.Equal(768, result.PixelHeight);
        Assert.Equal(PixelFormats.Bgr24, result.Format);
        Assert.True(result.IsFrozen);
    }

    [Fact]
    public void Crop_TakesPixelsFromRequestedPosition()
    {
        var source = TestImages.CreateGradient(1024, 1365);

        var result = _cropper.Crop(source, 0, 300);

        Assert.Equal(TestImages.GetPixel(source, 0, 300), TestImages.GetPixel(result, 0, 0));
        Assert.Equal(TestImages.GetPixel(source, 1023, 1067), TestImages.GetPixel(result, 1023, 767));
    }

    [Fact]
    public void Crop_TooSmallImage_Throws()
    {
        var source = TestImages.CreateGradient(800, 600);
        Assert.Throws<ArgumentException>(() => _cropper.Crop(source, 0, 0));
    }
}
