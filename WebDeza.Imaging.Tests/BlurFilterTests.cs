using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

public class BlurFilterTests
{
    private readonly BlurFilter _filter = new();

    private static ParameterValues Radius(double radius) => ParameterValues.Empty.With(BlurFilter.RadiusKey, radius);

    private ImageBuffer Blur(ImageBuffer image, double radius, double pixelScale = 1)
        => _filter.Apply(image, Radius(radius), new FilterContext(pixelScale, CancellationToken.None));

    // ---------------------------------------------------------------- 記述

    [Fact]
    public void Descriptor_MatchesDesign()
    {
        Assert.Equal("blur", _filter.Id);
        Assert.Equal("ぼかし", _filter.DisplayName);
        var radius = Assert.Single(_filter.Parameters);
        Assert.Equal((BlurFilter.RadiusKey, ParameterKind.Number, 0.0, 100.0, 0.5, "px"),
            (radius.Key, radius.Kind, radius.Min, radius.Max, radius.Step, radius.Unit));
        Assert.Equal(0.0, Convert.ToDouble(radius.Default));
    }

    // ---------------------------------------------------------------- E34

    [Fact]
    public void RadiusZero_IsIdentity()
    {
        Assert.True(_filter.IsIdentity(ParameterValues.Empty));
        Assert.True(_filter.IsIdentity(Radius(0)));
        Assert.True(_filter.IsIdentity(Radius(-5))); // 範囲外は 0 に丸める
        Assert.False(_filter.IsIdentity(Radius(0.5)));

        var image = TestBuffers.Distinct(8, 6);
        Assert.Same(image, Blur(image, 0));
    }

    [Theory]
    [InlineData(0xFF336699u)]   // 不透明
    [InlineData(0x80C86432u)]   // 半透明
    [InlineData(0x00000000u)]   // 透明
    public void SolidColor_DoesNotChange(uint bgra)
    {
        var image = PasteTests.Solid(30, 20, bgra);

        var result = Blur(image, 12);

        TestBuffers.AssertSameImage(image, result);
    }

    [Fact]
    public void Edge_BecomesSmooth_AndLargerRadiusSpreadsWider()
    {
        var image = BlackWhite(80, 10);

        var small = Blur(image, 4);
        var large = Blur(image, 16);

        Assert.Equal((80, 10), (small.Width, small.Height));
        foreach (var result in new[] { small, large })
        {
            // 左から右へ、明るさが減らずに変わる（なだらか）。端は黒と白のまま
            for (int x = 1; x < 80; x++) Assert.True(Red(result, x, 5) >= Red(result, x - 1, 5));
            Assert.Equal(0, Red(result, 0, 5));
            Assert.Equal(255, Red(result, 79, 5));
            Assert.True(result.IsOpaque);
        }
        // 黒でも白でもない画素の数は、半径 16 のほうが多い
        Assert.True(GrayCount(large) > GrayCount(small));
        Assert.True(GrayCount(small) > 0);
    }

    [Fact]
    public void Impulse_SpreadsWithVarianceOfGaussian()
    {
        // 黒い 1 行の中央に白い 1 画素。半径 8 → σ = 4 → 分散 16
        var pixels = new byte[201 * 4];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        pixels[100 * 4] = pixels[100 * 4 + 1] = pixels[100 * 4 + 2] = 255;
        var image = ImageBuffer.Create(201, 1, pixels, isOpaque: true);

        var result = Blur(image, 8);

        double total = 0, moment = 0;
        for (int x = 0; x < 201; x++)
        {
            int v = Red(result, x, 0);
            total += v;
            moment += v * (x - 100.0) * (x - 100.0);
        }
        Assert.InRange(total, 250, 260);           // 明るさの合計は保たれる（丸めの誤差を除く）
        Assert.InRange(moment / total, 14.5, 17.5); // 分散がほぼ σ²
        Assert.Equal(Red(result, 90, 0), Red(result, 110, 0)); // 左右対称
    }

    [Fact]
    public void SmallRadius_StillBlursSlightly()
    {
        // 半径 0.5 でも、端数の重みでわずかにぼける（整数の箱だけだと変わらない）
        var image = BlackWhite(10, 4);

        var result = Blur(image, 0.5);

        Assert.True(Red(result, 4, 1) > 0 || Red(result, 5, 1) < 255);
    }

    [Fact]
    public void RadiusLargerThanImage_Works()
    {
        var image = BlackWhite(6, 3);

        var result = Blur(image, 100);

        // 端を延ばすので、左が暗く右が明るいまま、全体がなだらかになる
        Assert.True(Red(result, 0, 1) < Red(result, 5, 1));
        Assert.True(Red(result, 0, 1) > 0);
    }

    // ---------------------------------------------------------------- E35: 透過

    [Fact]
    public void TransparentBlack_DoesNotBleedIntoOpaqueWhite()
    {
        // 左半分が透明な黒 (0,0,0,0)、右半分が不透明な白
        var pixels = new byte[40 * 10 * 4];
        for (int y = 0; y < 10; y++)
        for (int x = 20; x < 40; x++)
        {
            int i = (y * 40 + x) * 4;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 255;
        }
        var image = ImageBuffer.Create(40, 10, pixels);

        var result = Blur(image, 8);

        for (int x = 0; x < 40; x++)
        {
            int i = (5 * 40 + x) * 4;
            byte alpha = result.Pixels[i + 3];
            if (alpha == 0) continue;
            // 色の付いている画素は、どこでも白のまま（灰色にならない）
            Assert.Equal((255, 255, 255), (result.Pixels[i], result.Pixels[i + 1], result.Pixels[i + 2]));
        }
        // アルファは境目でなだらかになる
        for (int x = 1; x < 40; x++) Assert.True(result.Pixels[(5 * 40 + x) * 4 + 3] >= result.Pixels[(5 * 40 + x - 1) * 4 + 3]);
        Assert.InRange(result.Pixels[(5 * 40 + 19) * 4 + 3], 1, 254);
        Assert.False(result.IsOpaque);
    }

    // ---------------------------------------------------------------- E36: PixelScale

    [Fact]
    public void PixelScale_DividesRadius()
    {
        var image = TestBuffers.Distinct(50, 40);

        var scaled = Blur(image, 8, pixelScale: 2);
        var direct = Blur(image, 4, pixelScale: 1);

        TestBuffers.AssertSameImage(direct, scaled);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidPixelScale_Throws(double pixelScale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Blur(TestBuffers.Distinct(4, 4), 4, pixelScale));
    }

    // ---------------------------------------------------------------- そのほか

    [Fact]
    public void Input_IsNotChanged()
    {
        var image = TestBuffers.Distinct(20, 20);
        var original = (byte[])image.Pixels.Clone();

        Blur(image, 5);

        Assert.Equal(original, image.Pixels);
    }

    [Fact]
    public void Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            _filter.Apply(TestBuffers.Distinct(10, 10), Radius(4), new FilterContext(1, cts.Token)));
    }

    [Fact]
    public void LargeImage_IsProcessedInStrips()
    {
        // 列のまとまり（32 列）の境目をまたぐ幅で、横と縦の両方がつながっていることを確かめる
        var image = BlackWhite(100, 70);

        var result = Blur(image, 6);

        for (int y = 1; y < 70; y++)
        for (int x = 0; x < 100; x++)
            Assert.Equal(Red(result, x, 0), Red(result, x, y)); // 縦には変化が無い画像なので、どの行も同じ
    }

    // ---------------------------------------------------------------- 補助

    /// <summary>左半分が不透明な黒、右半分が不透明な白</summary>
    private static ImageBuffer BlackWhite(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            byte v = x < width / 2 ? (byte)0 : (byte)255;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
            pixels[i + 3] = 255;
        }
        return ImageBuffer.Create(width, height, pixels, isOpaque: true);
    }

    private static int Red(ImageBuffer image, int x, int y) => image.Pixels[(y * image.Width + x) * 4 + 2];

    private static int GrayCount(ImageBuffer image)
    {
        int count = 0;
        for (int x = 0; x < image.Width; x++)
        {
            int v = Red(image, x, image.Height / 2);
            if (v is > 0 and < 255) count++;
        }
        return count;
    }
}
