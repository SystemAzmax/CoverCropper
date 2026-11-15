using System.Windows.Media;
using System.Windows.Media.Imaging;
using WebDeza.Imaging.Wic;

namespace WebDeza.Imaging.Tests;

public class BitmapSourceConverterTests
{
    [Fact] // W08
    public void RoundTrip_KeepsAllPixels()
    {
        foreach (var image in new[] { WicTestHelper.WithAlpha(60, 40), TestBuffers.DistinctOpaque(60, 40), TestBuffers.Distinct(1, 1) })
        {
            var bitmap = BitmapSourceConverter.ToBitmapSource(image);
            var back = BitmapSourceConverter.FromBitmapSource(bitmap);

            TestBuffers.AssertSameImage(image, back);
            Assert.Equal(image.IsOpaque, back.IsOpaque);
        }
    }

    [Fact]
    public void ToBitmapSource_Is96DpiBgra32Frozen()
    {
        var bitmap = BitmapSourceConverter.ToBitmapSource(WicTestHelper.WithAlpha(60, 40));

        Assert.Equal((60, 40), (bitmap.PixelWidth, bitmap.PixelHeight));
        Assert.Equal(PixelFormats.Bgra32, bitmap.Format);
        Assert.Equal((96.0, 96.0), (bitmap.DpiX, bitmap.DpiY));
        Assert.True(bitmap.IsFrozen);
    }

    [Fact]
    public void ToBgr24BitmapSource_DropsAlphaAndKeepsColor()
    {
        var image = TestBuffers.DistinctOpaque(60, 40);

        var bitmap = BitmapSourceConverter.ToBgr24BitmapSource(image);

        Assert.Equal(PixelFormats.Bgr24, bitmap.Format);
        Assert.Equal((96.0, 96.0), (bitmap.DpiX, bitmap.DpiY));
        Assert.True(bitmap.IsFrozen);
        // Bgr24 → ImageBuffer に戻すと、アルファ 255 で同じ色になる
        TestBuffers.AssertSameImage(image, BitmapSourceConverter.FromBitmapSource(bitmap));
    }

    [Fact]
    public void FromBitmapSource_ConvertsOtherPixelFormatsAndIgnoresDpi()
    {
        // 300 DPI の Bgr24 と、16 bit グレースケール
        var opaque = TestBuffers.DistinctOpaque(60, 40);
        var bgr24 = BitmapSourceConverter.ToBgr24BitmapSource(opaque);
        var pixels = new byte[60 * 40 * 3];
        bgr24.CopyPixels(pixels, 60 * 3, 0);
        var highDpi = BitmapSource.Create(60, 40, 300, 300, PixelFormats.Bgr24, null, pixels, 60 * 3);

        TestBuffers.AssertSameImage(opaque, BitmapSourceConverter.FromBitmapSource(highDpi));

        var gray16 = new FormatConvertedBitmap(bgr24, PixelFormats.Gray16, null, 0);
        var gray = BitmapSourceConverter.FromBitmapSource(gray16);
        Assert.Equal((60, 40), (gray.Width, gray.Height));
        Assert.True(gray.IsOpaque);
        Assert.Equal(gray.Pixels[0], gray.Pixels[1]); // R = G = B
        Assert.Equal(gray.Pixels[0], gray.Pixels[2]);
    }

    [Fact]
    public void FromBitmapSource_TallImage_CopiesAllBands()
    {
        // 256 行ずつ写すので、行数が 256 の倍数でない画像で確かめる
        var image = TestBuffers.Distinct(7, 600);

        TestBuffers.AssertSameImage(image, BitmapSourceConverter.FromBitmapSource(BitmapSourceConverter.ToBitmapSource(image)));
    }

    [Fact]
    public void FromBitmapSource_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var bitmap = BitmapSourceConverter.ToBitmapSource(TestBuffers.Distinct(8, 8));

        Assert.ThrowsAny<OperationCanceledException>(() => BitmapSourceConverter.FromBitmapSource(bitmap, cts.Token));
    }
}
