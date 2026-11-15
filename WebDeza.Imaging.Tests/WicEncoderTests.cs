using System.IO;
using System.Windows.Media.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Imaging.Wic;

namespace WebDeza.Imaging.Tests;

public class WicEncoderTests
{
    private readonly FormatRegistry _registry = WicTestHelper.CreateRegistry();

    private static ParameterValues Quality(int quality) => ParameterValues.Empty.With(WicFormats.QualityKey, quality);

    private static ParameterValues Compression(string value) => ParameterValues.Empty.With(WicFormats.CompressionKey, value);

    // ---------------------------------------------------------------- W05: JPEG

    [Fact]
    public void Jpeg_HigherQuality_IsLarger()
    {
        var photo = WicTestHelper.Photo(320, 240);

        var q50 = WicTestHelper.Encode(_registry, "jpeg", photo, Quality(50));
        var q95 = WicTestHelper.Encode(_registry, "jpeg", photo, Quality(95));

        Assert.True(q95.Length > q50.Length, $"品質 95: {q95.Length} バイト、品質 50: {q50.Length} バイト");
        var decoded = WicTestHelper.Decode(_registry, "jpeg", q95);
        Assert.Equal((320, 240), (decoded.Image.Width, decoded.Image.Height));
        Assert.Equal("jpeg", decoded.FormatId);
    }

    [Fact]
    public void Jpeg_HasNoMetadata()
    {
        var bytes = WicTestHelper.Encode(_registry, "jpeg", WicTestHelper.Photo(64, 48), Quality(85));

        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        Assert.IsType<JpegBitmapDecoder>(decoder);
        Assert.Null(decoder.Thumbnail);
        Assert.Null(frame.Thumbnail);
        Assert.True(frame.ColorContexts is null || frame.ColorContexts.Count == 0);
        if (frame.Metadata is BitmapMetadata metadata)
        {
            Assert.False(metadata.ContainsQuery("/app1"));  // EXIF（位置情報など）
            Assert.False(metadata.ContainsQuery("/xmp"));
            Assert.False(metadata.ContainsQuery("/app13")); // IPTC
        }
    }

    [Fact]
    public void Jpeg_DefaultQualityIs85_AndOutOfRangeIsClamped()
    {
        var photo = WicTestHelper.Photo(64, 48);

        Assert.Equal(WicTestHelper.Encode(_registry, "jpeg", photo, Quality(85)), WicTestHelper.Encode(_registry, "jpeg", photo));
        Assert.Equal(WicTestHelper.Encode(_registry, "jpeg", photo, Quality(100)), WicTestHelper.Encode(_registry, "jpeg", photo, Quality(500)));
    }

    [Fact]
    public void Jpeg_MatchesDirectWpfEncodingOfBgr24()
    {
        // CoverCropper の JpegSaver（Bgr24 の BitmapSource を品質 85・メタデータなしで書く）と同じバイト列になる
        var photo = WicTestHelper.Photo(320, 240);
        var reference = new JpegBitmapEncoder { QualityLevel = 85 };
        reference.Frames.Add(BitmapFrame.Create(BitmapSourceConverter.ToBgr24BitmapSource(photo), null, null, null));
        using var stream = new MemoryStream();
        reference.Save(stream);

        Assert.Equal(stream.ToArray(), WicTestHelper.Encode(_registry, "jpeg", photo, Quality(85)));
    }

    // ---------------------------------------------------------------- W06: 書いて読み直す

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Png_RoundTrip_KeepsAllPixelsIncludingAlpha(bool interlace)
    {
        var options = ParameterValues.Empty.With(WicFormats.InterlaceKey, interlace);
        foreach (var image in new[] { WicTestHelper.WithAlpha(60, 40), TestBuffers.DistinctOpaque(60, 40) })
        {
            var decoded = WicTestHelper.Decode(_registry, "png", WicTestHelper.Encode(_registry, "png", image, options));

            TestBuffers.AssertSameImage(image, decoded.Image);
            Assert.Equal(image.IsOpaque, decoded.Image.IsOpaque);
        }
    }

    [Fact]
    public void Png_InterlaceOption_ChangesOutput()
    {
        var image = WicTestHelper.Photo(64, 48);

        var plain = WicTestHelper.Encode(_registry, "png", image, ParameterValues.Empty.With(WicFormats.InterlaceKey, false));
        var interlaced = WicTestHelper.Encode(_registry, "png", image, ParameterValues.Empty.With(WicFormats.InterlaceKey, true));

        Assert.NotEqual(plain, interlaced);
        Assert.Equal(plain, WicTestHelper.Encode(_registry, "png", image)); // 既定はオフ
    }

    [Theory]
    [InlineData(WicFormats.CompressionNone)]
    [InlineData(WicFormats.CompressionLzw)]
    [InlineData(WicFormats.CompressionZip)]
    public void Tiff_RoundTrip_KeepsAllPixels(string compression)
    {
        foreach (var image in new[] { WicTestHelper.WithAlpha(60, 40), TestBuffers.DistinctOpaque(60, 40) })
        {
            var decoded = WicTestHelper.Decode(_registry, "tiff", WicTestHelper.Encode(_registry, "tiff", image, Compression(compression)));

            TestBuffers.AssertSameImage(image, decoded.Image);
        }
    }

    [Fact]
    public void Tiff_CompressionOption_ChangesOutput_AndDefaultIsLzw()
    {
        var image = TestBuffers.DistinctOpaque(120, 80);

        var none = WicTestHelper.Encode(_registry, "tiff", image, Compression(WicFormats.CompressionNone));
        var lzw = WicTestHelper.Encode(_registry, "tiff", image, Compression(WicFormats.CompressionLzw));
        var zip = WicTestHelper.Encode(_registry, "tiff", image, Compression(WicFormats.CompressionZip));

        Assert.NotEqual(none, lzw);
        Assert.NotEqual(lzw, zip);
        Assert.True(none.Length > zip.Length);
        Assert.Equal(lzw, WicTestHelper.Encode(_registry, "tiff", image));
        // 選択肢に無い値は既定値（LZW）
        Assert.Equal(lzw, WicTestHelper.Encode(_registry, "tiff", image, Compression("jpeg")));
    }

    [Fact]
    public void Bmp_RoundTrip_KeepsAllPixelsOfOpaqueImage()
    {
        var image = TestBuffers.DistinctOpaque(61, 40); // 幅が 4 の倍数でない（行の詰め物がある）

        var decoded = WicTestHelper.Decode(_registry, "bmp", WicTestHelper.Encode(_registry, "bmp", image));

        TestBuffers.AssertSameImage(image, decoded.Image);
        Assert.Equal("bmp", decoded.FormatId);
    }

    // ---------------------------------------------------------------- W09: 同じ入力なら同じバイト列

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("bmp")]
    [InlineData("tiff")]
    public void Encode_SameImageAndOptions_ProducesSameBytes(string formatId)
    {
        var supportsAlpha = _registry.FindById(formatId)!.Capabilities.HasFlag(FormatCapabilities.Alpha);
        var image = supportsAlpha ? WicTestHelper.WithAlpha(160, 120) : WicTestHelper.Photo(160, 120);

        var first = WicTestHelper.Encode(_registry, formatId, image);
        var second = WicTestHelper.Encode(_registry, formatId, image);
        // 中身が同じ別の画像でも同じ
        var copy = ImageBuffer.Create(image.Width, image.Height, (byte[])image.Pixels.Clone());
        var third = WicTestHelper.Encode(_registry, formatId, copy);

        Assert.True(first.Length > 0);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }

    // ---------------------------------------------------------------- そのほか

    [Fact]
    public void Encode_DoesNotModifyImage()
    {
        var image = WicTestHelper.WithAlpha(60, 40);
        var before = (byte[])image.Pixels.Clone();

        foreach (var format in _registry.Encodable)
            WicTestHelper.Encode(_registry, format.Id, image);

        Assert.Equal(before, image.Pixels);
    }

    [Fact]
    public void Encode_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = new MemoryStream();

        Assert.ThrowsAny<OperationCanceledException>(() => _registry.FindById("png")!.Encoder!
            .Encode(TestBuffers.Distinct(8, 8), ParameterValues.Empty, stream, cts.Token));
        Assert.Equal(0, stream.Length);
    }
}
