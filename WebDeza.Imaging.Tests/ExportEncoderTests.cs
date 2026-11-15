using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Operations;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

/// <summary>ExportEncoder（偽の形式で。実際の JPEG・PNG では段階 3 の 3-4 で確かめる）</summary>
public class ExportEncoderTests
{
    private static readonly RgbColor White = new(255, 255, 255);

    /// <summary>JPEG に似た形式（透過なし・品質で SizeTuning）と、PNG に似た形式（透過あり・SizeTuning なし）</summary>
    private static (ExportEncoder Encoder, FakeEncoder Jpeg, FakeEncoder Png) Create()
    {
        var jpeg = new FakeEncoder();
        var png = new FakeEncoder
        {
            Options = new[] { new ParameterDescriptor("interlace", "インターレース", ParameterKind.Boolean, false) },
            SizeTuning = null,
            SizeOf = options => options.Get<bool>("interlace") ? 1200 : 1000,
        };
        var registry = new FormatRegistry();
        registry.Register(new FakeFormat("jpeg", FormatCapabilities.Decode | FormatCapabilities.Encode, ".jpg") { Encoder = jpeg });
        registry.Register(new FakeFormat("png", FormatCapabilities.Decode | FormatCapabilities.Encode | FormatCapabilities.Alpha, ".png") { Encoder = png });
        registry.Register(new FakeFormat("gif", FormatCapabilities.Decode, ".gif"));
        return (new ExportEncoder(registry), jpeg, png);
    }

    private static ExportSettings Settings(string formatId, long? limit = null, params (string FormatId, ParameterValues Options)[] options)
        => new(formatId, options.ToDictionary(o => o.FormatId, o => o.Options), limit, OutputSize.OriginalSize, White);

    private static readonly ImageBuffer Opaque = TestBuffers.DistinctOpaque(8, 6);

    // ---------------------------------------------------------------- E30

    [Fact]
    public void Encode_SizeTuningFormatWithLimit_UsesLargestQualityWithinLimit()
    {
        var (encoder, jpeg, _) = Create();

        var encoded = encoder.Encode(Opaque, Settings("jpeg", limit: 555, ("jpeg", ParameterValues.Empty.With("quality", 85))));

        Assert.True(encoded.Bytes.Length <= 555);
        Assert.Equal(550, encoded.Bytes.Length);
        Assert.Equal(55, encoded.UsedOptions.Get<int>("quality")); // 指定の 85 ではなく、探した品質
        Assert.True(encoded.FitsSizeLimit);
        Assert.Equal("jpeg", encoded.FormatId);
        Assert.InRange(jpeg.Calls.Count, 1, 8);
    }

    [Fact]
    public void Encode_FormatWithoutSizeTuning_IgnoresLimitAndUsesItsOptions()
    {
        var (encoder, _, png) = Create();
        var settings = Settings("png", limit: 10,
            ("png", ParameterValues.Empty.With("interlace", true)),
            ("jpeg", ParameterValues.Empty.With("quality", 40)));

        var encoded = encoder.Encode(Opaque, settings);

        Assert.Equal(1200, encoded.Bytes.Length); // 上限 10 バイトは使わない
        Assert.Equal(ParameterValues.Empty.With("interlace", true), encoded.UsedOptions);
        Assert.True(encoded.FitsSizeLimit);       // 上限を使わないので、警告（M26）は出さない
        Assert.Equal("png", encoded.FormatId);
        Assert.Single(png.Calls);
    }

    // ---------------------------------------------------------------- オプション

    [Fact]
    public void Encode_WithoutLimit_UsesQualityFromSettings()
    {
        var (encoder, jpeg, _) = Create();

        var encoded = encoder.Encode(Opaque, Settings("jpeg", null, ("jpeg", ParameterValues.Empty.With("quality", 70))));

        Assert.Equal(700, encoded.Bytes.Length);
        Assert.Equal(70, encoded.UsedOptions.Get<int>("quality"));
        Assert.True(encoded.FitsSizeLimit);
        Assert.Single(jpeg.Calls);
    }

    [Fact]
    public void Encode_OptionsAreNormalizedByEncoderDescriptors()
    {
        var (encoder, jpeg, _) = Create();

        // 範囲外（150）は 100 に丸め、知らないキーは捨てる
        var clamped = encoder.Encode(Opaque, Settings("jpeg", null, ("jpeg", ParameterValues.Empty.With("quality", 150).With("old", 1))));
        Assert.Equal(ParameterValues.Empty.With("quality", 100), clamped.UsedOptions);
        Assert.Equal(clamped.UsedOptions, jpeg.Calls[^1]);

        // その形式のオプションが無ければ既定値（85）
        var defaults = encoder.Encode(Opaque, Settings("jpeg"));
        Assert.Equal(85, defaults.UsedOptions.Get<int>("quality"));
        Assert.Equal(850, defaults.Bytes.Length);
    }

    [Fact]
    public void Encode_LimitTooSmall_ReturnsLowestQualityWithFitsFalse()
    {
        var (encoder, _, _) = Create();

        var encoded = encoder.Encode(Opaque, Settings("jpeg", limit: 5));

        Assert.False(encoded.FitsSizeLimit); // M26
        Assert.Equal(1, encoded.UsedOptions.Get<int>("quality"));
        Assert.Equal(10, encoded.Bytes.Length);
    }

    [Fact]
    public void Encode_LimitLargeEnough_UsesMaximumQualityNotSettings()
    {
        // 上限を指定したときは、指定の品質ではなく、上限に収まるいちばん高い品質（ここでは最大の 100）
        var (encoder, jpeg, _) = Create();

        var encoded = encoder.Encode(Opaque, Settings("jpeg", limit: 300 * 1024, ("jpeg", ParameterValues.Empty.With("quality", 60))));

        Assert.Equal(100, encoded.UsedOptions.Get<int>("quality"));
        Assert.Single(jpeg.Calls);
    }

    [Fact]
    public void Encode_SearchRangeComesFromTunedOptionDescriptor()
    {
        var limited = new FakeEncoder
        {
            Options = new[]
            {
                new ParameterDescriptor("level", "レベル", ParameterKind.Integer, 50, Min: 10, Max: 90),
                new ParameterDescriptor("fast", "速さ優先", ParameterKind.Boolean, true),
            },
            SizeTuning = new SizeTuning("level"),
            SizeOf = options => options.Get<int>("level") * 10,
        };
        var registry = new FormatRegistry();
        registry.Register(new FakeFormat("x", FormatCapabilities.Encode, ".x") { Encoder = limited });
        var encoder = new ExportEncoder(registry);

        var big = encoder.Encode(Opaque, Settings("x", limit: 100_000));
        var small = encoder.Encode(Opaque, Settings("x", limit: 1));

        Assert.Equal(90, big.UsedOptions.Get<int>("level"));     // 最大は記述の Max
        Assert.Equal(10, small.UsedOptions.Get<int>("level"));   // 最小は記述の Min
        Assert.False(small.FitsSizeLimit);
        Assert.True(big.UsedOptions.Get<bool>("fast"));          // ほかのオプションは保つ
        Assert.All(limited.Calls, c => Assert.InRange(c.Get<int>("level"), 10, 90));
    }

    [Fact]
    public void Encode_SizeTuningNotPointingToIntegerOption_Throws()
    {
        var broken = new FakeEncoder { SizeTuning = new SizeTuning("missing") };
        var registry = new FormatRegistry();
        registry.Register(new FakeFormat("x", FormatCapabilities.Encode, ".x") { Encoder = broken });

        Assert.Throws<InvalidOperationException>(() => new ExportEncoder(registry).Encode(Opaque, Settings("x", limit: 100)));
    }

    // ---------------------------------------------------------------- 透過

    [Fact]
    public void Encode_FormatWithoutAlpha_ReceivesOpaqueImage()
    {
        var (encoder, jpeg, _) = Create();
        var transparent = TestBuffers.Distinct(8, 6); // アルファが 253〜255
        var background = new RgbColor(10, 20, 30);

        encoder.Encode(transparent, Settings("jpeg") with { Background = background });

        Assert.True(jpeg.LastImage!.IsOpaque);
        TestBuffers.AssertSameImage(Flatten.Apply(transparent, background), jpeg.LastImage);
    }

    [Fact]
    public void Encode_FormatWithAlpha_ReceivesImageAsIs()
    {
        var (encoder, _, png) = Create();
        var transparent = TestBuffers.Distinct(8, 6);

        encoder.Encode(transparent, Settings("png"));

        Assert.Same(transparent, png.LastImage);
    }

    [Fact]
    public void Encode_OpaqueImage_IsPassedWithoutCopy()
    {
        var (encoder, jpeg, _) = Create();

        encoder.Encode(Opaque, Settings("jpeg"));

        Assert.Same(Opaque, jpeg.LastImage);
    }

    // ---------------------------------------------------------------- 形式

    [Theory]
    [InlineData("bmp")]   // 登録されていない
    [InlineData("gif")]   // 読み込みだけの形式
    [InlineData("JPEG")]  // Id は大文字小文字を区別する
    public void Encode_FormatNotEncodable_Throws(string formatId)
    {
        var (encoder, _, _) = Create();

        var ex = Assert.Throws<UnsupportedImageFormatException>(() => encoder.Encode(Opaque, Settings(formatId)));

        Assert.Equal(formatId, ex.FormatId);
    }

    [Fact]
    public void Encode_Cancelled_Throws()
    {
        var (encoder, _, _) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => encoder.Encode(Opaque, Settings("jpeg", limit: 555), cts.Token));
    }
}
