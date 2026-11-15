using WebDeza.Imaging.Editing;

namespace WebDeza.Imaging.Tests;

/// <summary>CropFrameCalculator の矩形の規則（Rules・Initial・Clamp・Center）</summary>
public class CropFrameRulesTests
{
    private static readonly AspectRatio Ratio4x3 = new(4, 3);

    private static FrameRules Rules(int width, int height, AspectRatio? aspect = null, int minLength = 1)
        => CropFrameCalculator.Rules(width, height, aspect, minLength);

    /// <summary>6000×2000 で 4:3・最小の長さ 96（最大 2667×2000、最小 128×96）</summary>
    internal static readonly FrameRules Rules4x3 = CropFrameCalculator.Rules(6000, 2000, new AspectRatio(4, 3), 96);

    // ---------------------------------------------------------------- E15: 最大の矩形

    [Theory]
    [InlineData(6000, 2000, 4, 3, 2667, 2000)]   // 横長: 高さいっぱい
    [InlineData(3000, 4000, 4, 3, 3000, 2250)]   // 縦長: 幅いっぱい
    [InlineData(4000, 3000, 4, 3, 4000, 3000)]   // 同じ比率: 画像全体
    [InlineData(1920, 1080, 1, 1, 1080, 1080)]   // 正方形
    public void Rules_MaxRect(int imageWidth, int imageHeight, int ratioWidth, int ratioHeight, int expectedWidth, int expectedHeight)
    {
        var rules = Rules(imageWidth, imageHeight, new AspectRatio(ratioWidth, ratioHeight));

        Assert.Equal((expectedWidth, expectedHeight), (rules.MaxWidth, rules.MaxHeight));
    }

    [Fact]
    public void Rules_Ratios()
    {
        var square = Rules(6000, 2000, new AspectRatio(1, 1));
        Assert.Equal(new AspectRatio(1, 1), square.Aspect);
        Assert.Equal((2000, 2000), (square.MaxWidth, square.MaxHeight));

        var portrait = Rules(6000, 2000, new AspectRatio(3, 4));
        Assert.Equal((1500, 2000), (portrait.MaxWidth, portrait.MaxHeight));

        // 元画像と同じ比率（呼び出し側が画像の幅:高さを渡す）
        var original = Rules(6000, 2000, new AspectRatio(6000, 2000));
        Assert.Equal(new AspectRatio(3, 1), original.Aspect);
        Assert.Equal((6000, 2000), (original.MaxWidth, original.MaxHeight));
    }

    [Fact]
    public void Rules_MaxRect_RoundsHalfAwayFromZero()
    {
        // 16:9 で 1001×1000: 幅いっぱいの 1001 で、高さ = 1001 × 9 ÷ 16 = 563.0625 → 563
        var wide = Rules(1001, 1000, new AspectRatio(16, 9));
        Assert.Equal((1001, 563), (wide.MaxWidth, wide.MaxHeight));

        // 3:2 で 1001×1000: 高さ = 1001 × 2 ÷ 3 = 667.33 → 667
        var r = Rules(1001, 1000, new AspectRatio(3, 2));
        Assert.Equal((1001, 667), (r.MaxWidth, r.MaxHeight));

        // 2:1 で 1001×1001 は幅いっぱいで、高さ = 500.5 → 501（0.5 は 0 から遠い方へ）
        var half = Rules(1001, 1001, new AspectRatio(2, 1));
        Assert.Equal((1001, 501), (half.MaxWidth, half.MaxHeight));
    }

    // ---------------------------------------------------------------- E16: 初期状態

    [Fact]
    public void Initial_CenteredMaxRect()
    {
        // (6000 − 2667) ÷ 2 = 1666.5 → 1666（端数は切り捨て）
        Assert.Equal(new PixelRect(1666, 0, 2667, 2000), CropFrameCalculator.Initial(Rules(6000, 2000, Ratio4x3)));
        Assert.Equal(new PixelRect(0, 875, 3000, 2250), CropFrameCalculator.Initial(Rules(3000, 4000, Ratio4x3)));
    }

    [Theory]
    [InlineData(6000, 2000)]
    [InlineData(1, 1)]
    [InlineData(10, 5000)]
    public void Initial_NoRatio_IsWholeImage(int width, int height)
    {
        var rules = Rules(width, height);

        Assert.Null(rules.Aspect);
        Assert.Equal(new PixelRect(0, 0, width, height), CropFrameCalculator.Initial(rules));
    }

    // ---------------------------------------------------------------- E18: 最小の矩形

    [Fact]
    public void Rules_MinRect_FixedRatio_KeepsBothSidesAtLeastMinLength()
    {
        // 4:3・最小の長さ 96: 幅 = ceil(96 × 4 ÷ 3) = 128、高さ 96
        Assert.Equal((128, 96), (Rules4x3.MinWidth, Rules4x3.MinHeight));

        // 1:2（縦長）なら、高さ 16 以上にするため幅は 8 ではなく 16（高さ 32）
        var tall = Rules(6000, 4000, new AspectRatio(1, 2), 16);
        Assert.Equal((16, 32), (tall.MinWidth, tall.MinHeight));

        // 16:9（横長）なら、高さ 16 以上にするため幅は ceil(16 × 16 ÷ 9) = 29（高さ round(16.31) = 16）
        var wide = Rules(6000, 4000, new AspectRatio(16, 9), 16);
        Assert.Equal((29, 16), (wide.MinWidth, wide.MinHeight));
    }

    [Fact]
    public void Rules_MinRect_LargerThanMax_UsesMax()
    {
        // 100×75 の画像に 4:3・最小の長さ 96: 最小の幅 128 は最大 100 を超えるので、最大に合わせる
        var rules = Rules(100, 75, Ratio4x3, 96);

        Assert.Equal((100, 75), (rules.MinWidth, rules.MinHeight));
        Assert.Equal((100, 75), (rules.MaxWidth, rules.MaxHeight));
    }

    [Fact]
    public void Rules_MinRect_NoRatio()
    {
        Assert.Equal((1, 1), (Rules(6000, 4000).MinWidth, Rules(6000, 4000).MinHeight));
        var rules16 = Rules(6000, 4000, null, 16);
        Assert.Equal((16, 16), (rules16.MinWidth, rules16.MinHeight));
    }

    [Theory]
    [InlineData(10, 300, 10, 16)]  // 幅だけ 16 より小さい
    [InlineData(300, 8, 16, 8)]    // 高さだけ 16 より小さい
    [InlineData(5, 5, 5, 5)]
    public void Rules_MinRect_NoRatio_SmallImage_UsesImageSize(int width, int height, int expectedMinWidth, int expectedMinHeight)
    {
        var rules = Rules(width, height, null, 16);

        Assert.Equal((expectedMinWidth, expectedMinHeight), (rules.MinWidth, rules.MinHeight));
    }

    [Fact]
    public void Rules_ExtremeRatio_KeepsAtLeastOnePixel()
    {
        // 512:1 を 100×100 の画像に: 高さは 0 にならず 1
        var rules = Rules(100, 100, new AspectRatio(8192, 16));

        Assert.Equal((100, 1), (rules.MaxWidth, rules.MaxHeight));
        Assert.Equal(new PixelRect(0, 49, 100, 1), CropFrameCalculator.Initial(rules));
    }

    [Fact]
    public void Rules_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules(0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules(100, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules(100, 100, null, minLength: 0));
        Assert.Throws<InvalidOperationException>(() => Rules(100, 100).HeightForWidth(50));
    }

    // ---------------------------------------------------------------- E17: 乱数

    [Fact]
    public void Rules_RandomInputs_RectsAreInsideImageWithinMinMaxAndKeepRatio()
    {
        var random = new Random(20261115);
        for (int i = 0; i < 1000; i++)
        {
            int width = RandomLength(random), height = RandomLength(random);
            var aspect = RandomAspect(random, width, height);
            int minLength = random.Next(2) == 0 ? 1 : random.Next(1, 200);
            var rules = Rules(width, height, aspect, minLength);
            string context = $"#{i}: 画像 {width}×{height}・{aspect}・最小 {minLength} → {rules}";

            Assert.True(rules.MinWidth >= 1 && rules.MinHeight >= 1, context);
            Assert.True(rules.MinWidth <= rules.MaxWidth && rules.MinHeight <= rules.MaxHeight, context);
            Assert.True(rules.MaxWidth <= width && rules.MaxHeight <= height, context);

            AssertFrame(CropFrameCalculator.Initial(rules), rules, context + "・初期状態");
            Assert.Equal((rules.MaxWidth, rules.MaxHeight), (CropFrameCalculator.Initial(rules).Width, CropFrameCalculator.Initial(rules).Height));

            // 規則に合わない矩形を Clamp・Center したものも、規則に合う
            var any = new PixelRect(random.Next(-500, width + 500), random.Next(-500, height + 500),
                random.Next(-10, width * 2 + 10), random.Next(-10, height * 2 + 10));
            AssertFrame(CropFrameCalculator.Clamp(any, rules), rules, context + $"・Clamp({any})");
            AssertFrame(CropFrameCalculator.Center(any, rules), rules, context + $"・Center({any})");
        }
    }

    /// <summary>矩形が画像の内側・最小〜最大にあり、比率が決まっていれば誤差が 0.5 px 以下（1 px に切り上げた場合を除く）</summary>
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

    /// <summary>比率なし・元画像と同じ・1〜20 の比率・とても細長い比率のどれか</summary>
    internal static AspectRatio? RandomAspect(Random random, int imageWidth, int imageHeight) => random.Next(4) switch
    {
        0 => null,
        1 => new AspectRatio(imageWidth, imageHeight),
        2 => new AspectRatio(random.Next(1, 21), random.Next(1, 21)),
        _ => new AspectRatio(random.Next(1, 10001), random.Next(1, 10001)),
    };

    // ---------------------------------------------------------------- Clamp・Center

    [Theory]
    [InlineData(-50, -10, 2667, 2000, 0, 0)]           // 左上にはみ出す
    [InlineData(5000, 100, 2667, 2000, 3333, 0)]       // 右下にはみ出す
    [InlineData(100, 0, 9999, 9999, 100, 0)]           // 大きすぎる → 最大（2667×2000）にして位置を収める
    [InlineData(1000, 500, 10, 10, 1000, 500)]         // 小さすぎる → 最小（128×96）
    public void Clamp_KeepsRectInsideImageWithinMinMax(int x, int y, int w, int h, int expectedX, int expectedY)
    {
        var clamped = CropFrameCalculator.Clamp(new PixelRect(x, y, w, h), Rules4x3);

        Assert.Equal((expectedX, expectedY), (clamped.X, clamped.Y));
        AssertFrame(clamped, Rules4x3, "Clamp");
    }

    [Fact]
    public void Clamp_FixedRatio_DerivesHeightFromWidth()
    {
        // 比率に合わない高さは、幅から求め直す（1200 × 3 ÷ 4 = 900）
        Assert.Equal(new PixelRect(100, 100, 1200, 900), CropFrameCalculator.Clamp(new PixelRect(100, 100, 1200, 50), Rules4x3));
        // 比率に合う矩形は変わらない
        var valid = new PixelRect(1666, 0, 2667, 2000);
        Assert.Equal(valid, CropFrameCalculator.Clamp(valid, Rules4x3));
    }

    [Fact]
    public void Clamp_NoRatio_ClampsWidthAndHeightSeparately()
    {
        var rules = Rules(600, 400, null, 16);

        // 幅は最大の 600、高さは最小の 16 に広げ、下端に収まるよう Y = 400 − 16 = 384
        Assert.Equal(new PixelRect(0, 384, 600, 16), CropFrameCalculator.Clamp(new PixelRect(-5, 395, 900, 10), rules));
        Assert.Equal(new PixelRect(10, 20, 30, 40), CropFrameCalculator.Clamp(new PixelRect(10, 20, 30, 40), rules));
    }

    [Fact]
    public void Center_KeepsSizeAndCenters()
    {
        // (6000 − 1200) ÷ 2 = 2400、(2000 − 900) ÷ 2 = 550
        Assert.Equal(new PixelRect(2400, 550, 1200, 900), CropFrameCalculator.Center(new PixelRect(0, 0, 1200, 900), Rules4x3));
        // 端数は切り捨て: (6000 − 1201) ÷ 2 = 2399.5 → 2399。高さは 1201 × 3 ÷ 4 = 900.75 → 901、(2000 − 901) ÷ 2 = 549.5 → 549
        Assert.Equal(new PixelRect(2399, 549, 1201, 901), CropFrameCalculator.Center(new PixelRect(5, 5, 1201, 901), Rules4x3));
    }
}
