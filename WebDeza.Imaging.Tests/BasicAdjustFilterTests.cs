using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

public class BasicAdjustFilterTests
{
    private readonly BasicAdjustFilter _filter = new();

    private ParameterValues Values(int brightness = 0, int contrast = 0, int saturation = 0)
        => ParameterValues.Defaults(_filter.Parameters)
            .With(BasicAdjustFilter.BrightnessKey, brightness)
            .With(BasicAdjustFilter.ContrastKey, contrast)
            .With(BasicAdjustFilter.SaturationKey, saturation);

    private ImageBuffer Apply(ImageBuffer source, ParameterValues values)
        => _filter.Apply(source, values, FilterContext.ForExport());

    // ---------------------------------------------------------------- 記述

    [Fact]
    public void Descriptor_MatchesDesign()
    {
        Assert.Equal("basic", _filter.Id);
        Assert.Equal("基本補正", _filter.DisplayName);
        Assert.Equal(new[] { "brightness", "contrast", "saturation" }, _filter.Parameters.Select(p => p.Key));
        Assert.Equal(new[] { "明るさ", "コントラスト", "彩度" }, _filter.Parameters.Select(p => p.DisplayName));
        Assert.All(_filter.Parameters, p =>
        {
            Assert.Equal(ParameterKind.Integer, p.Kind);
            Assert.Equal(-100, p.Min);
            Assert.Equal(100, p.Max);
            Assert.Equal(0, Convert.ToInt32(p.Default));
        });
    }

    // ---------------------------------------------------------------- E10: 既定値

    [Fact]
    public void IsIdentity_DefaultsOnly()
    {
        Assert.True(_filter.IsIdentity(ParameterValues.Defaults(_filter.Parameters)));
        Assert.True(_filter.IsIdentity(ParameterValues.Empty)); // 値が無いときは既定値として扱う
        Assert.True(_filter.IsIdentity(Values()));

        Assert.False(_filter.IsIdentity(Values(brightness: 1)));
        Assert.False(_filter.IsIdentity(Values(contrast: -1)));
        Assert.False(_filter.IsIdentity(Values(saturation: 100)));
    }

    [Fact]
    public void Apply_Defaults_ReturnsInput()
    {
        var source = TestBuffers.Distinct(8, 6);

        Assert.Same(source, Apply(source, Values()));
    }

    // ---------------------------------------------------------------- E10: 明るさ

    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    [InlineData(37)]
    public void Brightness_BlackAndWhiteDoNotChange(int brightness)
    {
        var result = Apply(Gray(0, 255), Values(brightness: brightness));

        Assert.Equal(0, result.Pixels[0]);
        Assert.Equal(255, result.Pixels[4]);
    }

    [Fact]
    public void Brightness_Plus100_Brightens128()
    {
        // γ = 0.5: 255 × √(128 ÷ 255) = 180.66… → 181
        Assert.Equal(181, Apply(Gray(128), Values(brightness: 100)).Pixels[0]);
    }

    [Fact]
    public void Brightness_Minus100_Darkens128()
    {
        // γ = 2: 255 × (128 ÷ 255)² = 64.25… → 64
        Assert.Equal(64, Apply(Gray(128), Values(brightness: -100)).Pixels[0]);
    }

    [Fact]
    public void Brightness_IsMonotonic()
    {
        // 明るさを上げるほど、どの値も同じか明るくなる
        var source = GrayRamp();
        byte[]? previous = null;
        for (int brightness = -100; brightness <= 100; brightness += 25)
        {
            var result = Apply(source, Values(brightness: brightness)).Pixels;
            if (previous is not null)
            {
                for (int i = 0; i < result.Length; i += 4)
                    Assert.True(result[i] >= previous[i], $"明るさ {brightness}: 値 {i / 4} が暗くなりました");
            }
            previous = result;
        }
    }

    // ---------------------------------------------------------------- E10: コントラスト

    [Theory]
    [InlineData(32, 0)]     // (32 − 127.5) × 2 + 127.5 = −63.5 → 0
    [InlineData(224, 255)]  // (224 − 127.5) × 2 + 127.5 = 320.5 → 255
    [InlineData(127, 127)]  // (127 − 127.5) × 2 + 127.5 = 126.5 → 127（0.5 は 0 から遠い方へ）
    [InlineData(128, 129)]  // 128.5 → 129
    public void Contrast_Plus100(byte value, byte expected)
    {
        Assert.Equal(expected, Apply(Gray(value), Values(contrast: 100)).Pixels[0]);
    }

    [Theory]
    [InlineData(0, 64)]     // (0 − 127.5) × 0.5 + 127.5 = 63.75 → 64
    [InlineData(255, 191)]  // (255 − 127.5) × 0.5 + 127.5 = 191.25 → 191
    [InlineData(128, 128)]  // 127.75 → 128
    public void Contrast_Minus100(byte value, byte expected)
    {
        Assert.Equal(expected, Apply(Gray(value), Values(contrast: -100)).Pixels[0]);
    }

    // ---------------------------------------------------------------- E10: 彩度

    [Fact]
    public void Saturation_Minus100_MakesGray()
    {
        var source = TestBuffers.Distinct(40, 30);

        var result = Apply(source, Values(saturation: -100));

        for (int i = 0; i < result.Pixels.Length; i += 4)
        {
            Assert.Equal(result.Pixels[i], result.Pixels[i + 1]);
            Assert.Equal(result.Pixels[i], result.Pixels[i + 2]);
        }
    }

    [Fact]
    public void Saturation_Minus100_UsesLuma()
    {
        // 純粋な赤: Y = 0.299 × 255 = 76.245 → 76。緑: 0.587 × 255 = 149.685 → 150。青: 0.114 × 255 = 29.07 → 29
        var source = Pixels((0, 0, 255, 255), (0, 255, 0, 255), (255, 0, 0, 255));

        var result = Apply(source, Values(saturation: -100));

        Assert.Equal(76, result.Pixels[0]);
        Assert.Equal(150, result.Pixels[4]);
        Assert.Equal(29, result.Pixels[8]);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(-50)]
    [InlineData(1)]
    public void Saturation_GrayDoesNotChange(int saturation)
    {
        var source = GrayRamp();

        var result = Apply(source, Values(saturation: saturation));

        Assert.Equal(source.Pixels, result.Pixels);
    }

    [Fact]
    public void Saturation_Plus100_DoublesDistanceFromLuma()
    {
        // (B, G, R) = (100, 100, 160): Y = 0.299 × 160 + 0.587 × 100 + 0.114 × 100 = 117.94
        //   R: 117.94 + (160 − 117.94) × 2 = 202.06 → 202
        //   G・B: 117.94 + (100 − 117.94) × 2 = 82.06 → 82
        var result = Apply(Pixels((100, 100, 160, 255)), Values(saturation: 100));

        Assert.Equal(new byte[] { 82, 82, 202, 255 }, result.Pixels);
    }

    // ---------------------------------------------------------------- 計算の順番と丸め

    [Fact]
    public void BrightnessAndContrast_AreRoundedOnlyOnce()
    {
        // 明るさ 50・コントラスト 50 を、実数のまま続けて計算した値と全段で一致する
        var source = GrayRamp();

        var result = Apply(source, Values(brightness: 50, contrast: 50));

        for (int v = 0; v < 256; v++)
        {
            double value = 255 * Math.Pow(v / 255.0, Math.Pow(2, -0.5));
            value = (value - 127.5) * Math.Pow(2, 0.5) + 127.5;
            byte expected = (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
            Assert.Equal(expected, result.Pixels[v * 4]);
        }
    }

    [Fact]
    public void Saturation_IsComputedFromToneAdjustedValues()
    {
        // コントラストを先にかけ、その結果（丸めた値）から彩度を計算する
        var source = Pixels((60, 120, 200, 255));
        // コントラスト 40: k = 2^0.4。(v − 127.5) × k + 127.5 を丸めた値
        double k = Math.Pow(2, 0.4);
        double Tone(int v) => Math.Clamp(Math.Round((v - 127.5) * k + 127.5, MidpointRounding.AwayFromZero), 0, 255);
        double b = Tone(60), g = Tone(120), r = Tone(200);
        double luma = 0.299 * r + 0.587 * g + 0.114 * b;
        byte Expected(double c) => (byte)Math.Clamp(Math.Round(luma + (c - luma) * 0.7, MidpointRounding.AwayFromZero), 0, 255);

        var result = Apply(source, Values(contrast: 40, saturation: -30));

        Assert.Equal(new byte[] { Expected(b), Expected(g), Expected(r), 255 }, result.Pixels);
    }

    [Fact]
    public void Values_OutOfRangeOrMissing_AreNormalized()
    {
        var source = GrayRamp();

        // 範囲外は ±100 に丸める
        var clamped = Apply(source, ParameterValues.Empty.With(BasicAdjustFilter.ContrastKey, 500));
        Assert.Equal(Apply(source, Values(contrast: 100)).Pixels, clamped.Pixels);

        // 無いキーは既定値（0）
        var partial = Apply(source, ParameterValues.Empty.With(BasicAdjustFilter.BrightnessKey, 30));
        Assert.Equal(Apply(source, Values(brightness: 30)).Pixels, partial.Pixels);
    }

    // ---------------------------------------------------------------- E11: 透過

    [Fact]
    public void Apply_KeepsAlpha()
    {
        var source = TestBuffers.Distinct(40, 30); // アルファが 253〜255 で変わる
        var translucent = Pixels((10, 20, 30, 0), (200, 100, 50, 1), (90, 180, 240, 128), (255, 255, 255, 254));

        foreach (var image in new[] { source, translucent })
        {
            var result = Apply(image, Values(brightness: 60, contrast: -40, saturation: 80));

            for (int i = 3; i < image.Pixels.Length; i += 4)
                Assert.Equal(image.Pixels[i], result.Pixels[i]);
            Assert.Equal(image.IsOpaque, result.IsOpaque);
        }
    }

    // ---------------------------------------------------------------- そのほか

    [Fact]
    public void Apply_DoesNotModifyInput()
    {
        var source = TestBuffers.Distinct(40, 30);
        var before = (byte[])source.Pixels.Clone();

        var result = Apply(source, Values(brightness: 20, contrast: 20, saturation: 20));

        Assert.NotSame(source.Pixels, result.Pixels);
        Assert.Equal(before, source.Pixels);
        Assert.Equal((source.Width, source.Height), (result.Width, result.Height));
    }

    [Fact]
    public void Apply_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => _filter.Apply(GrayRamp(), Values(brightness: 10), new FilterContext(1, cts.Token)));
    }

    // ---------------------------------------------------------------- 補助

    /// <summary>灰色（R = G = B）の画素を横に並べた画像</summary>
    private static ImageBuffer Gray(params byte[] values)
        => Pixels(values.Select(v => (v, v, v, (byte)255)).ToArray());

    /// <summary>0〜255 の灰色を 1 つずつ並べた 256×1 の画像</summary>
    private static ImageBuffer GrayRamp() => Gray(Enumerable.Range(0, 256).Select(v => (byte)v).ToArray());

    /// <summary>(B, G, R, A) の画素を横に並べた画像</summary>
    private static ImageBuffer Pixels(params (byte B, byte G, byte R, byte A)[] pixels)
    {
        var data = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            data[i * 4] = pixels[i].B;
            data[i * 4 + 1] = pixels[i].G;
            data[i * 4 + 2] = pixels[i].R;
            data[i * 4 + 3] = pixels[i].A;
        }
        return ImageBuffer.Create(pixels.Length, 1, data);
    }
}
