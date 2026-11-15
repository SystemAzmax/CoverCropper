using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Imaging.Wic;

namespace WebDeza.Imaging.Tests;

public class WicFormatsTests
{
    private const FormatCapabilities D = FormatCapabilities.Decode;
    private const FormatCapabilities E = FormatCapabilities.Encode;
    private const FormatCapabilities A = FormatCapabilities.Alpha;

    private readonly FormatRegistry _registry = WicTestHelper.CreateRegistry();

    // ---------------------------------------------------------------- 設計書 5.4 の表

    [Theory]
    [InlineData("jpeg", "JPEG", D | E, ".jpg .jpeg .jpe .jfif", false)]
    [InlineData("png", "PNG", D | E | A, ".png", false)]
    [InlineData("bmp", "BMP", D | E, ".bmp .dib", false)]
    [InlineData("tiff", "TIFF", D | E | A, ".tif .tiff", false)]
    [InlineData("gif", "GIF", D | A, ".gif", false)]
    [InlineData("jxr", "JPEG XR", D | A, ".jxr .wdp", false)]
    [InlineData("ico", "ICO", D | A, ".ico", false)]
    [InlineData("webp", "WebP", D | A, ".webp", true)]
    [InlineData("heif", "HEIF", D, ".heic .heif", true)]
    [InlineData("avif", "AVIF", D | A, ".avif", true)]
    public void AddWicFormats_RegistersFormatsOfDesignTable(
        string id, string displayName, FormatCapabilities capabilities, string extensions, bool hasRequirement)
    {
        var format = _registry.FindById(id);

        Assert.NotNull(format);
        Assert.Equal(displayName, format.DisplayName);
        Assert.Equal(capabilities, format.Capabilities);
        Assert.Equal(extensions.Split(' '), format.Extensions);
        Assert.Equal(hasRequirement, format.Requirement is not null);
        Assert.NotNull(format.Decoder);
        Assert.Equal(capabilities.HasFlag(E), format.Encoder is not null);
        foreach (var extension in format.Extensions)
            Assert.Same(format, _registry.FindByExtension(extension.ToUpperInvariant()));
    }

    [Fact]
    public void AddWicFormats_OrderAndLists()
    {
        Assert.Equal(new[] { "jpeg", "png", "bmp", "tiff", "gif", "jxr", "ico", "webp", "heif", "avif" },
            _registry.All.Select(f => f.Id));
        Assert.Equal(10, _registry.Decodable.Count);
        Assert.Equal(new[] { "jpeg", "png", "bmp", "tiff" }, _registry.Encodable.Select(f => f.Id)); // 先頭の JPEG が既定
        // 書き出すときの拡張子は先頭のもの
        Assert.Equal(new[] { ".jpg", ".png", ".bmp", ".tif" }, _registry.Encodable.Select(f => f.Extensions[0]));
    }

    [Fact]
    public void AddWicFormats_Twice_Throws()
    {
        Assert.Throws<ArgumentException>(() => _registry.AddWicFormats());
    }

    [Fact]
    public void EncoderOptions_MatchDesignTable()
    {
        var jpeg = _registry.FindById("jpeg")!.Encoder!;
        var quality = Assert.Single(jpeg.Options);
        Assert.Equal(("quality", ParameterKind.Integer, 1.0, 100.0), (quality.Key, quality.Kind, quality.Min, quality.Max));
        Assert.Equal(85, ParameterValues.Defaults(jpeg.Options).Get<int>("quality"));
        Assert.Equal(new SizeTuning("quality"), jpeg.SizeTuning);

        var png = _registry.FindById("png")!.Encoder!;
        var interlace = Assert.Single(png.Options);
        Assert.Equal(("interlace", ParameterKind.Boolean), (interlace.Key, interlace.Kind));
        Assert.False(ParameterValues.Defaults(png.Options).Get<bool>("interlace"));
        Assert.Null(png.SizeTuning);

        var bmp = _registry.FindById("bmp")!.Encoder!;
        Assert.Empty(bmp.Options);
        Assert.Null(bmp.SizeTuning);

        var tiff = _registry.FindById("tiff")!.Encoder!;
        var compression = Assert.Single(tiff.Options);
        Assert.Equal(("compression", ParameterKind.Choice), (compression.Key, compression.Kind));
        Assert.Equal(new[] { "none", "lzw", "zip" }, compression.Choices!.Select(c => c.Value));
        Assert.Equal(new[] { "なし", "LZW", "ZIP" }, compression.Choices!.Select(c => c.DisplayName));
        Assert.Equal("lzw", ParameterValues.Defaults(tiff.Options).Get<string>("compression"));
        Assert.Null(tiff.SizeTuning);
    }

    // ---------------------------------------------------------------- E30: 実際の JPEG・PNG

    private static ExportSettings Settings(string formatId, long? limit, ParameterValues? options = null) => new(
        formatId,
        options is null ? new Dictionary<string, ParameterValues>() : new Dictionary<string, ParameterValues> { [formatId] = options },
        limit, OutputSize.OriginalSize, new RgbColor(255, 255, 255));

    [Fact]
    public void ExportEncoder_JpegWithLimit_FitsLimitAndReportsFoundQuality()
    {
        var photo = WicTestHelper.Photo(400, 300);
        var encoder = new ExportEncoder(_registry);
        int smallest = WicTestHelper.Encode(_registry, "jpeg", photo, ParameterValues.Empty.With("quality", 1)).Length;
        int largest = WicTestHelper.Encode(_registry, "jpeg", photo, ParameterValues.Empty.With("quality", 100)).Length;
        long limit = (smallest + largest) / 2;

        var encoded = encoder.Encode(photo, Settings("jpeg", limit, ParameterValues.Empty.With("quality", 85)));

        int quality = encoded.UsedOptions.Get<int>("quality");
        Assert.True(encoded.FitsSizeLimit);
        Assert.True(encoded.Bytes.Length <= limit, $"{encoded.Bytes.Length} バイト > 上限 {limit}");
        Assert.InRange(quality, 2, 99);
        Assert.Equal("jpeg", encoded.FormatId);
        // バイト列は、見つけた品質でエンコードしたものそのもの（表示するサイズ = 書き出すファイルのサイズ）
        Assert.Equal(WicTestHelper.Encode(_registry, "jpeg", photo, ParameterValues.Empty.With("quality", quality)), encoded.Bytes);
        // 1 つ上の品質では上限を超える（この画像ではサイズが品質とともに増える）
        Assert.True(WicTestHelper.Encode(_registry, "jpeg", photo, ParameterValues.Empty.With("quality", quality + 1)).Length > limit);
    }

    [Fact]
    public void ExportEncoder_JpegLimitTooSmall_UsesLowestQualityAndReportsNotFitting()
    {
        var photo = WicTestHelper.Photo(400, 300);

        var encoded = new ExportEncoder(_registry).Encode(photo, Settings("jpeg", limit: 100));

        Assert.False(encoded.FitsSizeLimit);
        Assert.Equal(1, encoded.UsedOptions.Get<int>("quality"));
        Assert.True(encoded.Bytes.Length > 100);
    }

    [Fact]
    public void ExportEncoder_PngWithLimit_IgnoresLimitAndUsesPngOptions()
    {
        var image = WicTestHelper.WithAlpha(120, 80);
        var options = ParameterValues.Empty.With("interlace", true);

        var encoded = new ExportEncoder(_registry).Encode(image, Settings("png", limit: 100, options));

        Assert.True(encoded.Bytes.Length > 100);
        Assert.True(encoded.FitsSizeLimit);
        Assert.Equal(options, encoded.UsedOptions);
        Assert.Equal(WicTestHelper.Encode(_registry, "png", image, options), encoded.Bytes);
        // 透過が残っている
        TestBuffers.AssertSameImage(image, WicTestHelper.Decode(_registry, "png", encoded.Bytes).Image);
    }

    [Fact]
    public void ExportEncoder_JpegFromTransparentImage_IsFlattenedWithBackground()
    {
        // 全面が透明な画像を、背景が赤の設定で JPEG に → 赤い画像になる
        var transparent = ImageBuffer.Create(32, 32, new byte[32 * 32 * 4]);
        var settings = Settings("jpeg", null, ParameterValues.Empty.With("quality", 100)) with { Background = new RgbColor(255, 0, 0) };

        var encoded = new ExportEncoder(_registry).Encode(transparent, settings);

        var decoded = WicTestHelper.Decode(_registry, "jpeg", encoded.Bytes).Image;
        Assert.InRange(decoded.Pixels[2], 250, 255); // R
        Assert.InRange(decoded.Pixels[1], 0, 5);     // G
        Assert.InRange(decoded.Pixels[0], 0, 5);     // B
    }
}
