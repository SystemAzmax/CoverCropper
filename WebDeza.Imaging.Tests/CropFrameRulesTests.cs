using WebDeza.Imaging.Editing;

namespace WebDeza.Imaging.Tests;

/// <summary>CropFrameCalculator の枠の規則（Rules・Initial・Clamp・Center・OutputPixelSize）</summary>
public class CropFrameRulesTests
{
    private static readonly OutputSize Cover = OutputSize.ExactSize(1024, 768);

    private static FrameRules Rules(int width, int height, OutputSize size, CropAspect? aspect = null)
        => CropFrameCalculator.Rules(width, height, aspect ?? CropAspect.Free, size);

    // ---------------------------------------------------------------- E15: 最大の枠

    [Theory]
    [InlineData(6000, 2000, 1024, 768, 2667, 2000)]   // 横長: 高さいっぱい
    [InlineData(3000, 4000, 1024, 768, 3000, 2250)]   // 縦長: 幅いっぱい
    [InlineData(4000, 3000, 1024, 768, 4000, 3000)]   // 同じ比率: 画像全体
    [InlineData(1920, 1080, 1080, 1080, 1080, 1080)]  // 正方形
    public void Rules_MaxFrame(int imageWidth, int imageHeight, int outputWidth, int outputHeight, int expectedWidth, int expectedHeight)
    {
        var rules = Rules(imageWidth, imageHeight, OutputSize.ExactSize(outputWidth, outputHeight));

        Assert.Equal((expectedWidth, expectedHeight), (rules.MaxWidth, rules.MaxHeight));
    }

    [Fact]
    public void Rules_Exact_IgnoresCropAspectSelection()
    {
        // 書き出しサイズが「幅×高さ」のときは、比率の選択（自由・1:1 など）は使わない
        var free = Rules(6000, 2000, Cover, CropAspect.Free);
        var square = Rules(6000, 2000, Cover, CropAspect.Fixed(1, 1));

        Assert.Equal(new AspectRatio(4, 3), free.Aspect);
        Assert.Equal(free, square);
    }

    [Fact]
    public void Rules_FixedAndOriginal_UseSelectedRatio()
    {
        var longSide = OutputSize.LongSideOf(1920);

        var square = Rules(6000, 2000, longSide, CropAspect.Fixed(1, 1));
        Assert.Equal(new AspectRatio(1, 1), square.Aspect);
        Assert.Equal((2000, 2000), (square.MaxWidth, square.MaxHeight));

        var portrait = Rules(6000, 2000, OutputSize.OriginalSize, CropAspect.Fixed(4, 3).Swapped());
        Assert.Equal((1500, 2000), (portrait.MaxWidth, portrait.MaxHeight));

        var original = Rules(6000, 2000, longSide, CropAspect.Original);
        Assert.Equal(new AspectRatio(3, 1), original.Aspect);
        Assert.Equal((6000, 2000), (original.MaxWidth, original.MaxHeight));
    }

    [Fact]
    public void Rules_MaxFrame_RoundsHalfAwayFromZero()
    {
        // 16:9 で 1001×1000: 幅いっぱいの 1001 で、高さ = 1001 × 9 ÷ 16 = 563.0625 → 563
        var wide = Rules(1001, 1000, OutputSize.LongSideOf(1920), CropAspect.Fixed(16, 9));
        Assert.Equal((1001, 563), (wide.MaxWidth, wide.MaxHeight));

        // 3:2 で 1001×1000: 高さ = 1001 × 2 ÷ 3 = 667.33 → 667
        var r = Rules(1001, 1000, OutputSize.LongSideOf(1920), CropAspect.Fixed(3, 2));
        Assert.Equal((1001, 667), (r.MaxWidth, r.MaxHeight));

        // 2:1 で 1001×1001 は幅いっぱいで、高さ = 500.5 → 501（0.5 は 0 から遠い方へ）
        var half = Rules(1001, 1001, OutputSize.LongSideOf(1920), CropAspect.Fixed(2, 1));
        Assert.Equal((1001, 501), (half.MaxWidth, half.MaxHeight));
    }

    // ---------------------------------------------------------------- E16: 初期状態

    [Fact]
    public void Initial_CenteredMaxFrame()
    {
        // (6000 − 2667) ÷ 2 = 1666.5 → 1666（端数は切り捨て）
        Assert.Equal(new PixelRect(1666, 0, 2667, 2000), CropFrameCalculator.Initial(Rules(6000, 2000, Cover)));
        Assert.Equal(new PixelRect(0, 875, 3000, 2250), CropFrameCalculator.Initial(Rules(3000, 4000, Cover)));
    }

    [Theory]
    [InlineData(6000, 2000)]
    [InlineData(1, 1)]
    [InlineData(10, 5000)]
    public void Initial_Free_IsWholeImage(int width, int height)
    {
        foreach (var size in new[] { OutputSize.LongSideOf(1920), OutputSize.OriginalSize })
        {
            var rules = Rules(width, height, size, CropAspect.Free);

            Assert.Null(rules.Aspect);
            Assert.Equal(new PixelRect(0, 0, width, height), CropFrameCalculator.Initial(rules));
        }
    }

    // ---------------------------------------------------------------- E18: 最小の枠

    [Fact]
    public void Rules_MinFrame_ExactLimitsUpscaleTo8x()
    {
        // ceil(1024 ÷ 8) = 128。高さは 128 × 3 ÷ 4 = 96
        var rules = Rules(6000, 4000, Cover);

        Assert.Equal((128, 96), (rules.MinWidth, rules.MinHeight));
    }

    [Fact]
    public void Rules_MinFrame_LargerThanMax_UsesMax()
    {
        // 100×75 の画像に 1024×768: 最小の幅 128 は最大 100 を超えるので、最大に合わせる
        var rules = Rules(100, 75, Cover);

        Assert.Equal((100, 75), (rules.MinWidth, rules.MinHeight));
        Assert.Equal((100, 75), (rules.MaxWidth, rules.MaxHeight));
    }

    [Fact]
    public void Rules_MinFrame_Free_Is16x16()
    {
        var rules = Rules(6000, 4000, OutputSize.LongSideOf(1920));

        Assert.Equal((16, 16), (rules.MinWidth, rules.MinHeight));
    }

    [Theory]
    [InlineData(10, 300, 10, 16)]  // 幅だけ 16 より小さい
    [InlineData(300, 8, 16, 8)]    // 高さだけ 16 より小さい
    [InlineData(5, 5, 5, 5)]
    public void Rules_MinFrame_Free_SmallImage_UsesImageSize(int width, int height, int expectedMinWidth, int expectedMinHeight)
    {
        var rules = Rules(width, height, OutputSize.OriginalSize);

        Assert.Equal((expectedMinWidth, expectedMinHeight), (rules.MinWidth, rules.MinHeight));
    }

    [Fact]
    public void Rules_MinFrame_FixedRatio_KeepsBothSidesAtLeast16()
    {
        // 1:2（縦長）なら、高さ 16 以上にするため幅は 8 ではなく 16（高さ 32）
        var tall = Rules(6000, 4000, OutputSize.LongSideOf(1920), CropAspect.Fixed(1, 2));
        Assert.Equal((16, 32), (tall.MinWidth, tall.MinHeight));

        // 16:9（横長）なら、高さ 16 以上にするため幅は ceil(16 × 16 ÷ 9) = 29（高さ round(16.31) = 16）
        var wide = Rules(6000, 4000, OutputSize.LongSideOf(1920), CropAspect.Fixed(16, 9));
        Assert.Equal((29, 16), (wide.MinWidth, wide.MinHeight));
    }

    [Fact]
    public void Rules_ExtremeRatio_KeepsAtLeastOnePixel()
    {
        // 書き出しサイズ 8192×16（512:1）を 100×100 の画像に: 高さは 0 にならず 1
        var rules = Rules(100, 100, OutputSize.ExactSize(8192, 16));

        Assert.Equal((100, 1), (rules.MaxWidth, rules.MaxHeight));
        Assert.Equal(new PixelRect(0, 49, 100, 1), CropFrameCalculator.Initial(rules));
    }

    [Fact]
    public void Rules_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CropFrameCalculator.Rules(0, 100, CropAspect.Free, Cover));
        Assert.Throws<ArgumentOutOfRangeException>(() => CropFrameCalculator.Rules(100, -1, CropAspect.Free, Cover));
        Assert.Throws<ArgumentException>(() => CropFrameCalculator.Rules(100, 100, CropAspect.Free, OutputSize.ExactSize(0, 768)));
        Assert.Throws<InvalidOperationException>(() => Rules(100, 100, OutputSize.OriginalSize).HeightForWidth(50));
    }

    // ---------------------------------------------------------------- E17: 乱数

    [Fact]
    public void Rules_RandomInputs_FramesAreInsideImageWithinMinMaxAndKeepRatio()
    {
        var random = new Random(20261115);
        for (int i = 0; i < 1000; i++)
        {
            int width = RandomLength(random), height = RandomLength(random);
            var aspect = RandomAspect(random);
            var size = RandomOutputSize(random);
            var rules = CropFrameCalculator.Rules(width, height, aspect, size);
            string context = $"#{i}: 画像 {width}×{height}・{aspect}・{size} → {rules}";

            Assert.True(rules.MinWidth >= 1 && rules.MinHeight >= 1, context);
            Assert.True(rules.MinWidth <= rules.MaxWidth && rules.MinHeight <= rules.MaxHeight, context);
            Assert.True(rules.MaxWidth <= width && rules.MaxHeight <= height, context);

            AssertFrame(CropFrameCalculator.Initial(rules), rules, context + "・初期状態");
            Assert.Equal((rules.MaxWidth, rules.MaxHeight), (CropFrameCalculator.Initial(rules).Width, CropFrameCalculator.Initial(rules).Height));

            // 規則に合わない枠を Clamp・Center したものも、規則に合う
            var any = new PixelRect(random.Next(-500, width + 500), random.Next(-500, height + 500),
                random.Next(-10, width * 2 + 10), random.Next(-10, height * 2 + 10));
            AssertFrame(CropFrameCalculator.Clamp(any, rules), rules, context + $"・Clamp({any})");
            AssertFrame(CropFrameCalculator.Center(any, rules), rules, context + $"・Center({any})");
        }
    }

    /// <summary>枠が画像の内側・最小〜最大にあり、比率が決まっていれば誤差が 0.5 px 以下（1 px に切り上げた場合を除く）</summary>
    internal static void AssertFrame(PixelRect rect, FrameRules rules, string context)
    {
        Assert.True(rect.X >= 0 && rect.Y >= 0
            && (long)rect.X + rect.Width <= rules.ImageWidth
            && (long)rect.Y + rect.Height <= rules.ImageHeight, $"{context}: {rect} が画像の外に出ています");
        Assert.True(rect.Width >= rules.MinWidth && rect.Width <= rules.MaxWidth
            && rect.Height >= rules.MinHeight && rect.Height <= rules.MaxHeight, $"{context}: {rect} が最小〜最大の外です");

        if (rules.Aspect is not { } a || rect.Width == 1 || rect.Height == 1) return;
        // 幅から求めた高さ、または高さから求めた幅の、どちらかの誤差が 0.5 px 以下
        double heightError = Math.Abs(rect.Height - (double)rect.Width * a.Height / a.Width);
        double widthError = Math.Abs(rect.Width - (double)rect.Height * a.Width / a.Height);
        Assert.True(Math.Min(heightError, widthError) <= 0.5 + 1e-9,
            $"{context}: {rect} の比率の誤差が大きすぎます（高さ {heightError:F3} px・幅 {widthError:F3} px）");
    }

    internal static int RandomLength(Random random) => random.Next(4) switch
    {
        0 => random.Next(1, 64),       // とても小さい画像
        1 => random.Next(64, 2000),
        _ => random.Next(2000, 20000),
    };

    internal static CropAspect RandomAspect(Random random) => random.Next(3) switch
    {
        0 => CropAspect.Free,
        1 => CropAspect.Original,
        _ => CropAspect.Fixed(random.Next(1, 21), random.Next(1, 21)),
    };

    internal static OutputSize RandomOutputSize(Random random) => random.Next(3) switch
    {
        0 => OutputSize.ExactSize(random.Next(16, 8193), random.Next(16, 8193)),
        1 => OutputSize.LongSideOf(random.Next(16, 8193)),
        _ => OutputSize.OriginalSize,
    };

    // ---------------------------------------------------------------- Clamp・Center

    [Theory]
    [InlineData(-50, -10, 2667, 2000, 0, 0)]           // 左上にはみ出す
    [InlineData(5000, 100, 2667, 2000, 3333, 0)]       // 右下にはみ出す
    [InlineData(100, 0, 9999, 9999, 100, 0)]           // 大きすぎる → 最大（2667×2000）にして位置を収める
    [InlineData(1000, 500, 10, 10, 1000, 500)]         // 小さすぎる → 最小（128×96）
    public void Clamp_KeepsFrameInsideImageWithinMinMax(int x, int y, int w, int h, int expectedX, int expectedY)
    {
        var rules = Rules(6000, 2000, Cover);

        var clamped = CropFrameCalculator.Clamp(new PixelRect(x, y, w, h), rules);

        Assert.Equal((expectedX, expectedY), (clamped.X, clamped.Y));
        AssertFrame(clamped, rules, "Clamp");
    }

    [Fact]
    public void Clamp_FixedRatio_DerivesHeightFromWidth()
    {
        var rules = Rules(6000, 2000, Cover);

        // 比率に合わない高さは、幅から求め直す（1200 × 3 ÷ 4 = 900）
        Assert.Equal(new PixelRect(100, 100, 1200, 900), CropFrameCalculator.Clamp(new PixelRect(100, 100, 1200, 50), rules));
        // 比率に合う枠は変わらない
        var valid = new PixelRect(1666, 0, 2667, 2000);
        Assert.Equal(valid, CropFrameCalculator.Clamp(valid, rules));
    }

    [Fact]
    public void Clamp_Free_ClampsWidthAndHeightSeparately()
    {
        var rules = Rules(600, 400, OutputSize.OriginalSize);

        // 幅は最大の 600、高さは最小の 16 に広げ、下端に収まるよう Y = 400 − 16 = 384
        Assert.Equal(new PixelRect(0, 384, 600, 16), CropFrameCalculator.Clamp(new PixelRect(-5, 395, 900, 10), rules));
        Assert.Equal(new PixelRect(10, 20, 30, 40), CropFrameCalculator.Clamp(new PixelRect(10, 20, 30, 40), rules));
    }

    [Fact]
    public void Center_KeepsSizeAndCenters()
    {
        var rules = Rules(6000, 2000, Cover);

        // (6000 − 1200) ÷ 2 = 2400、(2000 − 900) ÷ 2 = 550
        Assert.Equal(new PixelRect(2400, 550, 1200, 900), CropFrameCalculator.Center(new PixelRect(0, 0, 1200, 900), rules));
        // 端数は切り捨て: (6000 − 1201) ÷ 2 = 2399.5 → 2399。高さは 1201 × 3 ÷ 4 = 900.75 → 901、(2000 − 901) ÷ 2 = 549.5 → 549
        Assert.Equal(new PixelRect(2399, 549, 1201, 901), CropFrameCalculator.Center(new PixelRect(5, 5, 1201, 901), rules));
    }

    // ---------------------------------------------------------------- E22: 書き出す大きさ

    [Fact]
    public void OutputPixelSize_Exact_IsSpecifiedSize()
    {
        Assert.Equal((1024, 768), CropFrameCalculator.OutputPixelSize(new PixelRect(1666, 0, 2667, 2000), Cover));
        Assert.Equal((1024, 768), CropFrameCalculator.OutputPixelSize(new PixelRect(0, 0, 100, 75), Cover)); // 拡大になっても指定の大きさ
    }

    [Theory]
    [InlineData(3000, 2000, 1920, 1920, 1280)]  // E22
    [InlineData(2000, 3000, 1920, 1280, 1920)]  // 縦長
    [InlineData(1000, 1000, 1920, 1920, 1920)]  // 拡大
    [InlineData(3001, 2000, 1920, 1920, 1280)]  // 2000 × 1920 ÷ 3001 = 1279.57 → 1280
    [InlineData(3000, 1, 1920, 1920, 1)]        // 0.64 → 1（最低 1）
    [InlineData(5000, 3, 1920, 1920, 1)]        // 1.152 → 1
    [InlineData(4000, 7, 16, 16, 1)]            // 0.028 → 1（最低 1）
    public void OutputPixelSize_LongSide_ScalesLongestSide(int width, int height, int longSide, int expectedWidth, int expectedHeight)
    {
        var size = CropFrameCalculator.OutputPixelSize(new PixelRect(5, 5, width, height), OutputSize.LongSideOf(longSide));

        Assert.Equal((expectedWidth, expectedHeight), size);
    }

    [Fact]
    public void OutputPixelSize_Original_IsFrameSize()
    {
        Assert.Equal((2667, 2000), CropFrameCalculator.OutputPixelSize(new PixelRect(1666, 0, 2667, 2000), OutputSize.OriginalSize));
    }

    [Fact]
    public void OutputPixelSize_EmptyFrame_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CropFrameCalculator.OutputPixelSize(new PixelRect(0, 0, 0, 10), Cover));
    }
}
