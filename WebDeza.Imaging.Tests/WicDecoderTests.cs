using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Geometry;
using WebDeza.Imaging.Wic;

namespace WebDeza.Imaging.Tests;

public class WicDecoderTests
{
    private readonly FormatRegistry _registry = WicTestHelper.CreateRegistry();

    private static readonly ImageBuffer Opaque = TestBuffers.DistinctOpaque(50, 40);

    private static BitmapSource OpaqueBitmap => BitmapSourceConverter.ToBgr24BitmapSource(Opaque);

    // ---------------------------------------------------------------- W01

    [Fact]
    public void Decode_LosslessFormats_ReturnExactPixels()
    {
        using var temp = new TempFolder();
        var files = new (string Name, BitmapEncoder Encoder, string FormatId)[]
        {
            ("a.png", new PngBitmapEncoder(), "png"),
            ("a.bmp", new BmpBitmapEncoder(), "bmp"),
            ("a.dib", new BmpBitmapEncoder(), "bmp"),
            ("a.tif", new TiffBitmapEncoder(), "tiff"),
            ("a.tiff", new TiffBitmapEncoder { Compression = TiffCompressOption.Zip }, "tiff"),
        };

        foreach (var (name, encoder, formatId) in files)
        {
            var path = WicTestHelper.SaveWithWpf(temp, name, encoder, OpaqueBitmap);

            var decoded = _registry.Decode(path);

            TestBuffers.AssertSameImage(Opaque, decoded.Image);
            Assert.True(decoded.Image.IsOpaque);
            Assert.Equal(formatId, decoded.FormatId);
            Assert.Equal(1, decoded.Metadata.AppliedExifOrientation);
            Assert.Equal(1, decoded.Metadata.FrameCount);
        }
    }

    [Fact]
    public void Decode_JpegGifJxr_ReturnCorrectSize()
    {
        using var temp = new TempFolder();
        var files = new (string Name, BitmapEncoder Encoder, string FormatId)[]
        {
            ("a.jpg", new JpegBitmapEncoder { QualityLevel = 90 }, "jpeg"),
            ("a.jfif", new JpegBitmapEncoder(), "jpeg"),
            ("a.gif", new GifBitmapEncoder(), "gif"),
            ("a.jxr", new WmpBitmapEncoder(), "jxr"),
        };

        foreach (var (name, encoder, formatId) in files)
        {
            var path = WicTestHelper.SaveWithWpf(temp, name, encoder, OpaqueBitmap);

            var decoded = _registry.Decode(path);

            Assert.Equal((50, 40), (decoded.Image.Width, decoded.Image.Height));
            Assert.Equal(50 * 40 * 4, decoded.Image.Pixels.Length); // Bgra32
            Assert.Equal(formatId, decoded.FormatId);
        }
    }

    [Fact]
    public void Decode_Ico_UsesLargestImage()
    {
        // CoverCropper のアイコン（16〜256 px の 10 サイズ入り）
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "AppIcon.ico");

        var decoded = _registry.Decode(path);

        Assert.Equal((256, 256), (decoded.Image.Width, decoded.Image.Height));
        Assert.Equal("ico", decoded.FormatId);
        Assert.Equal(10, decoded.Metadata.FrameCount);
    }

    [Fact]
    public void Decode_MultiFrameGifAndTiff_UseFirstFrame()
    {
        using var temp = new TempFolder();
        var second = BitmapSourceConverter.ToBgr24BitmapSource(TestBuffers.DistinctOpaque(20, 10));
        var tiff = new TiffBitmapEncoder();
        tiff.Frames.Add(BitmapFrame.Create(OpaqueBitmap));
        tiff.Frames.Add(BitmapFrame.Create(second));
        var path = temp.Combine("pages.tif");
        using (var stream = File.Create(path)) tiff.Save(stream);

        var decoded = _registry.Decode(path);

        TestBuffers.AssertSameImage(Opaque, decoded.Image); // 1 ページ目
        Assert.Equal(2, decoded.Metadata.FrameCount);
    }

    // ---------------------------------------------------------------- W02

    [Fact]
    public void Decode_JpegWithExifOrientation6_IsRotatedClockwise()
    {
        using var temp = new TempFolder();
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
        var source = BitmapSourceConverter.ToBgr24BitmapSource(TestBuffers.DistinctOpaque(200, 100));
        var path = WicTestHelper.SaveWithWpf(temp, "rotated.jpg", new JpegBitmapEncoder { QualityLevel = 100 }, source, metadata);

        var decoded = _registry.Decode(path);

        Assert.Equal((100, 200), (decoded.Image.Width, decoded.Image.Height));
        Assert.Equal(6, decoded.Metadata.AppliedExifOrientation);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public void Decode_TiffWithOrientation_AppliesItToPixels(int orientation)
    {
        // TIFF は可逆なので、向きを適用した画素まで確かめられる
        using var temp = new TempFolder();
        var metadata = new BitmapMetadata("tiff");
        metadata.SetQuery("/ifd/{ushort=274}", (ushort)orientation);
        var path = WicTestHelper.SaveWithWpf(temp, "rotated.tif", new TiffBitmapEncoder(), OpaqueBitmap, metadata);

        var decoded = _registry.Decode(path);

        TestBuffers.AssertSameImage(Orientation.ApplyExif(Opaque, orientation), decoded.Image);
        Assert.Equal(orientation, decoded.Metadata.AppliedExifOrientation);
    }

    // ---------------------------------------------------------------- W03

    [Fact]
    public void Decode_TransparentPng_KeepsAlpha()
    {
        using var temp = new TempFolder();
        var source = WicTestHelper.WithAlpha(60, 40);
        var path = WicTestHelper.SaveWithWpf(temp, "alpha.png", new PngBitmapEncoder(), BitmapSourceConverter.ToBitmapSource(source));

        var decoded = _registry.Decode(path);

        Assert.False(decoded.Image.IsOpaque);
        TestBuffers.AssertSameImage(source, decoded.Image); // 白で塗られず、透明な画素の色もそのまま
    }

    // ---------------------------------------------------------------- W04

    [Fact]
    public void Decode_DoesNotModifyOrLockSourceFile()
    {
        using var temp = new TempFolder();
        var path = WicTestHelper.SaveWithWpf(temp, "source.png", new PngBitmapEncoder(), OpaqueBitmap);
        var hashBefore = SHA256.HashData(File.ReadAllBytes(path));
        var writeTimeBefore = File.GetLastWriteTimeUtc(path);

        _registry.Decode(path);
        _registry.DecodeThumbnail(path, 16);

        Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(path));
        File.Delete(path); // ロックが残っていなければ削除できる
        Assert.False(File.Exists(path));
    }

    // ---------------------------------------------------------------- W07

    [Fact]
    public void Decode_ExtensionDiffersFromContent_ReadsByContent()
    {
        // 拡張子は .jpg だが、中身は PNG
        using var temp = new TempFolder();
        var path = WicTestHelper.SaveWithWpf(temp, "actually-png.jpg", new PngBitmapEncoder(), OpaqueBitmap);

        var decoded = _registry.Decode(path);

        TestBuffers.AssertSameImage(Opaque, decoded.Image);
        Assert.Equal("png", decoded.FormatId); // 中身から分かった形式
    }

    // ---------------------------------------------------------------- 読めない中身

    [Theory]
    [InlineData("a.jpg", false)]
    [InlineData("a.png", false)]
    [InlineData("a.gif", false)]
    [InlineData("a.webp", true)]   // OS の拡張機能が要る形式は、要るものの名前が入る
    [InlineData("a.heic", true)]
    [InlineData("a.avif", true)]
    public void Decode_ContentIsNotAnImage_ThrowsUnsupported(string name, bool hasRequirement)
    {
        using var temp = new TempFolder();
        var path = temp.WriteFile(name, "これは画像ではありません。not an image at all. 0123456789"u8.ToArray());

        var ex = Assert.Throws<UnsupportedImageFormatException>(() => _registry.Decode(path));

        Assert.IsAssignableFrom<NotSupportedException>(ex);
        Assert.Equal(hasRequirement, ex.Requirement is not null);
        Assert.Equal(_registry.FindByExtension(Path.GetExtension(name))!.Id, ex.FormatId);
        File.Delete(path); // 読めなかったときもロックが残らない
    }

    // ---------------------------------------------------------------- サムネイル

    [Fact]
    public void DecodeThumbnail_FitsLongSideAndAppliesOrientation()
    {
        using var temp = new TempFolder();
        var metadata = new BitmapMetadata("tiff");
        metadata.SetQuery("/ifd/{ushort=274}", (ushort)6);
        var source = BitmapSourceConverter.ToBgr24BitmapSource(TestBuffers.DistinctOpaque(400, 200));
        var path = WicTestHelper.SaveWithWpf(temp, "wide.tif", new TiffBitmapEncoder(), source, metadata);

        var thumbnail = _registry.DecodeThumbnail(path, 160);

        // 向きを直すと 200×400。長辺 160 に縮小して 80×160
        Assert.Equal((80, 160), (thumbnail.Width, thumbnail.Height));
    }

    [Fact]
    public void DecodeThumbnail_SmallImage_IsNotEnlarged()
    {
        using var temp = new TempFolder();
        var path = WicTestHelper.SaveWithWpf(temp, "small.png", new PngBitmapEncoder(), OpaqueBitmap);

        var thumbnail = _registry.DecodeThumbnail(path, 160);

        TestBuffers.AssertSameImage(Opaque, thumbnail);
    }

    [Fact]
    public void Decode_Cancelled_Throws()
    {
        using var temp = new TempFolder();
        var path = WicTestHelper.SaveWithWpf(temp, "a.png", new PngBitmapEncoder(), OpaqueBitmap);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => _registry.Decode(path, cts.Token));
    }

    // ---------------------------------------------------------------- スレッド

    /// <summary>WIC の HEIF のコンテナー形式（GUID_ContainerFormatHeif）</summary>
    private static readonly Guid HeifContainerFormat = new("E1E62521-6787-405B-A339-500715B5763F");

    [Fact]
    public async Task Decode_HeifFromThreadPool_DoesNotCrash()
    {
        // OS の拡張機能のコーデック（HEIF）は、MTA のスレッドで使うとプロセスごと落ちる。
        // 読み込みは STA のスレッドで行うので、スレッドプールから読んでも読めることを確かめる
        using var temp = new TempFolder();
        var path = temp.Combine("a.heic");
        if (!RunOnSta(() => TrySaveHeif(path))) return; // HEIF の拡張機能が無い環境では作れないので、確かめない

        var decoded = await Task.Run(() =>
        {
            Assert.Equal(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState());
            return _registry.Decode(path);
        });

        Assert.Equal((Opaque.Width, Opaque.Height), (decoded.Image.Width, decoded.Image.Height));
        Assert.Equal(WicFormats.HeifId, decoded.FormatId);
        File.Delete(path); // ロックが残らない
    }

    /// <summary>WPF の HEIF のエンコーダーで保存する。エンコーダーが無ければ false</summary>
    private static bool TrySaveHeif(string path)
    {
        BitmapEncoder encoder;
        try
        {
            encoder = BitmapEncoder.Create(HeifContainerFormat);
        }
        catch (NotSupportedException)
        {
            return false;
        }
        encoder.Frames.Add(BitmapFrame.Create(BitmapSourceConverter.ToBgr24BitmapSource(Opaque)));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return true;
    }

    private static T RunOnSta<T>(Func<T> func)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("STA のスレッドで失敗しました。", error);
        return result!;
    }
}
