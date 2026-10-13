using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoverCropper.Services;

namespace CoverCropper.Tests;

public class ImageLoaderTests
{
    private readonly ImageLoader _loader = new();

    [Fact]
    public void Load_TransparentPng_IsCompositedOnWhite()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("alpha.png");
        TestImages.Save(new PngBitmapEncoder(),
            TestImages.CreateSolid(40, 30, Color.FromArgb(0, 255, 0, 0), PixelFormats.Bgra32), path);

        var image = _loader.Load(path);

        Assert.Equal(PixelFormats.Bgr24, image.Format);
        Assert.Equal(new byte[] { 255, 255, 255 }, TestImages.GetPixel(image, 10, 10));
    }

    [Fact]
    public void Load_HalfTransparentPng_IsBlendedWithWhite()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("half.png");
        TestImages.Save(new PngBitmapEncoder(),
            TestImages.CreateSolid(10, 10, Color.FromArgb(128, 0, 0, 0), PixelFormats.Bgra32), path);

        var px = TestImages.GetPixel(_loader.Load(path), 5, 5);

        Assert.All(px, v => Assert.InRange(v, 125, 129));
    }

    [Fact]
    public void Load_NormalizesDpiTo96AndKeepsPixelSize()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("dpi.png");
        TestImages.Save(new PngBitmapEncoder(), TestImages.CreateGradient(321, 123, dpi: 300), path);

        var image = _loader.Load(path);

        Assert.Equal(321, image.PixelWidth);
        Assert.Equal(123, image.PixelHeight);
        Assert.Equal(96, image.DpiX);
        Assert.True(image.IsFrozen);
    }

    [Fact]
    public void Load_GrayscaleAndBmpAndGif_AreConvertedToBgr24()
    {
        using var temp = new TempFolder();
        var gray = new FormatConvertedBitmap(TestImages.CreateGradient(50, 40), PixelFormats.Gray16, null, 0);
        var files = new (BitmapEncoder Encoder, BitmapSource Image, string Name)[]
        {
            (new TiffBitmapEncoder(), gray, "gray.tif"),
            (new BmpBitmapEncoder(), TestImages.CreateGradient(50, 40), "a.bmp"),
            (new GifBitmapEncoder(), TestImages.CreateGradient(50, 40), "a.gif"),
        };

        foreach (var (encoder, image, name) in files)
        {
            var path = temp.Combine(name);
            TestImages.Save(encoder, image, path);

            var loaded = _loader.Load(path);

            Assert.Equal(PixelFormats.Bgr24, loaded.Format);
            Assert.Equal(50, loaded.PixelWidth);
            Assert.Equal(40, loaded.PixelHeight);
        }
    }

    [Fact]
    public void Load_JpegWithExifOrientation6_IsRotatedClockwise()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("rotated.jpg");
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
        var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
        encoder.Frames.Add(BitmapFrame.Create(TestImages.CreateGradient(200, 100), null, metadata, null));
        using (var stream = File.Create(path)) encoder.Save(stream);

        var image = _loader.Load(path);

        Assert.Equal(100, image.PixelWidth);
        Assert.Equal(200, image.PixelHeight);
    }

    [Fact]
    public void Load_DoesNotModifyOrLockSourceFile()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("source.png");
        TestImages.Save(new PngBitmapEncoder(), TestImages.CreateGradient(64, 48), path);
        var hashBefore = SHA256.HashData(File.ReadAllBytes(path));
        var writeTimeBefore = File.GetLastWriteTimeUtc(path);

        _loader.Load(path);

        Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(path));
        // ロックが解放されていればリネームできる
        var renamed = temp.Combine("renamed.png");
        File.Move(path, renamed);
        Assert.True(File.Exists(renamed));
    }

    [Fact]
    public void Load_UnsupportedExtension_Throws()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("a.txt");
        File.WriteAllText(path, "not an image");

        Assert.Throws<NotSupportedException>(() => _loader.Load(path));
    }

    [Theory]
    [InlineData(2, 3, 2, 2, 0)] // 左右反転: (0,0) ← (2,0)
    [InlineData(3, 3, 2, 2, 1)] // 180度: (0,0) ← (2,1)
    [InlineData(4, 3, 2, 0, 1)] // 上下反転: (0,0) ← (0,1)
    [InlineData(5, 2, 3, 0, 0)] // 転置: (0,0) ← (0,0)
    [InlineData(6, 2, 3, 0, 1)] // 時計回り: (0,0) ← 左下 (0,1)
    [InlineData(7, 2, 3, 2, 1)] // 反転置: (0,0) ← (2,1)
    [InlineData(8, 2, 3, 2, 0)] // 反時計回り: (0,0) ← 右上 (2,0)
    public void ApplyOrientation_MapsTopLeftPixelCorrectly(
        int orientation, int expectedW, int expectedH, int srcX, int srcY)
    {
        // 3×2 の画像。各ピクセルの B 値 = y*3 + x
        const int w = 3, h = 2;
        var src = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            src[(y * w + x) * 3] = (byte)(y * w + x);

        var (dst, dw, dh) = ImageLoader.ApplyOrientation(src, w, h, orientation);

        Assert.Equal(expectedW, dw);
        Assert.Equal(expectedH, dh);
        Assert.Equal(srcY * w + srcX, dst[0]);
    }
}
