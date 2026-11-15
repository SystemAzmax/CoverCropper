using WebDeza.Imaging.Editing;

namespace WebDeza.Imaging.Tests;

/// <summary>
/// 選択範囲用の矩形の計算（設計書 3.0 の 5.3。実装計画書 6-4）。
/// 規則は Rules(幅, 高さ, 比率, 最小の長さ) で作る（書き出しのサイズに縛られない）
/// </summary>
public class SelectionRectTests
{
    private static readonly AspectRatio Ratio4x3 = new(4, 3);

    private static FrameRules Rules(int width, int height, AspectRatio? aspect = null, int minLength = 1)
        => CropFrameCalculator.Rules(width, height, aspect, minLength);

    // ---------------------------------------------------------------- E15: 最大の矩形

    [Theory]
    [InlineData(6000, 2000, 4, 3, 2667, 2000)]
    [InlineData(3000, 4000, 4, 3, 3000, 2250)]
    [InlineData(4000, 3000, 4, 3, 4000, 3000)]
    [InlineData(1920, 1080, 1, 1, 1080, 1080)]
    public void Rules_MaxRect(int width, int height, int ratioWidth, int ratioHeight, int expectedWidth, int expectedHeight)
    {
        var rules = Rules(width, height, new AspectRatio(ratioWidth, ratioHeight));

        Assert.Equal((expectedWidth, expectedHeight), (rules.MaxWidth, rules.MaxHeight));
        Assert.Equal(new AspectRatio(ratioWidth, ratioHeight), rules.Aspect);
    }

    [Fact]
    public void Rules_NoRatio_MaxIsWholeImage()
    {
        foreach (var aspect in new AspectRatio?[] { null, default(AspectRatio) }) // default（0:0）も比率なし
        {
            var rules = Rules(600, 400, aspect);

            Assert.Null(rules.Aspect);
            Assert.Equal((600, 400), (rules.MaxWidth, rules.MaxHeight));
        }
    }

    // ---------------------------------------------------------------- E16: 初期状態

    [Fact]
    public void Initial_IsCenteredMaxRect()
    {
        Assert.Equal(new PixelRect(1666, 0, 2667, 2000), CropFrameCalculator.Initial(Rules(6000, 2000, Ratio4x3)));
        Assert.Equal(new PixelRect(0, 0, 600, 400), CropFrameCalculator.Initial(Rules(600, 400)));
    }

    // ---------------------------------------------------------------- E18: 最小の矩形

    [Fact]
    public void Rules_MinRect()
    {
        // 比率なし: 1×1（既定の最小の長さ）
        var free = Rules(600, 400);
        Assert.Equal((1, 1), (free.MinWidth, free.MinHeight));

        // 最小の長さ 16・比率 4:3: 幅は ceil(16 × 4 ÷ 3) = 22、高さは round(22 × 3 ÷ 4) = 17（どちらも 16 以上）
        var fixedRatio = Rules(600, 400, Ratio4x3, minLength: 16);
        Assert.Equal((22, 17), (fixedRatio.MinWidth, fixedRatio.MinHeight));

        // 画像が最小より小さい: 最大（画像に収まる大きさ）に合わせる
        var small = Rules(10, 8, Ratio4x3, minLength: 16);
        Assert.Equal((small.MaxWidth, small.MaxHeight), (small.MinWidth, small.MinHeight));
        var smallFree = Rules(10, 8, null, minLength: 16);
        Assert.Equal((10, 8), (smallFree.MinWidth, smallFree.MinHeight));
    }

    [Fact]
    public void Rules_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules(10, 10, null, minLength: 0));
    }

    // ---------------------------------------------------------------- E19: 移動

    [Fact]
    public void Move_ByPixels()
    {
        var rules = Rules(600, 400);
        var rect = new PixelRect(100, 50, 200, 100);

        Assert.Equal(rect with { X = 110 }, CropFrameCalculator.Move(rect, 10, 0, rules));
        Assert.Equal(rect with { X = 90, Y = 60 }, CropFrameCalculator.Move(rect, -10, 10, rules));
        Assert.Equal(rect, CropFrameCalculator.Move(rect, 0, 0, rules));
    }

    [Fact]
    public void Move_StopsAtImageEdge()
    {
        var rules = Rules(600, 400);
        var rect = new PixelRect(100, 50, 200, 100);

        Assert.Equal(rect with { X = 400 }, CropFrameCalculator.Move(rect, 1000, 0, rules));
        Assert.Equal(rect with { X = 0, Y = 0 }, CropFrameCalculator.Move(rect, -1000, -1000, rules));
        Assert.Equal(rect with { X = 400, Y = 300 }, CropFrameCalculator.Move(rect, int.MaxValue, int.MaxValue, rules)); // あふれない
        Assert.Equal(rect with { X = 0 }, CropFrameCalculator.Move(rect, int.MinValue, 0, rules));
    }

    // ---------------------------------------------------------------- E20・E21: 大きさ・中心・反転（新しい規則で）

    [Fact]
    public void ResizeFromHandle_WithNewRules()
    {
        var fixedRules = Rules(600, 400, Ratio4x3);
        var rect = new PixelRect(100, 100, 200, 150);

        // 比率固定で右下を外へ: 左上を固定し、画像の端で止まる
        var grown = CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.BottomRight, 2000, 2000, fixedRules);
        Assert.Equal((100, 100), (grown.X, grown.Y));
        Assert.Equal(400, grown.Y + grown.Height); // 下の端まで
        Assert.Equal(400, grown.Width);            // 高さ 300 に合う幅

        // 左上を越えて動かすと、最小（1 px と比率に合う大きさ）で止まり、裏返らない
        var shrunk = CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.BottomRight, 0, 0, fixedRules);
        Assert.Equal((100, 100, fixedRules.MinWidth, fixedRules.MinHeight), (shrunk.X, shrunk.Y, shrunk.Width, shrunk.Height));

        // 比率なしで右の辺: 幅だけ変わる
        var free = CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.Right, 450, 999, Rules(600, 400));
        Assert.Equal(new PixelRect(100, 100, 350, 150), free);
    }

    [Fact]
    public void ResizeAroundCenterAndMirror_WithNewRules()
    {
        var rules = Rules(600, 400, Ratio4x3);

        // 中心を保つ（端数は左上へ寄せる: Y = 137 + floor((150 − 75) ÷ 2) = 174）
        Assert.Equal(new PixelRect(250, 174, 100, 75),
            CropFrameCalculator.ResizeAroundCenter(new PixelRect(200, 137, 200, 150), 100, rules));
        Assert.Equal(new PixelRect(600 - 100 - 200, 50, 200, 150),
            CropFrameCalculator.Mirror(new PixelRect(100, 50, 200, 150), horizontal: true, rules));
    }

    // ---------------------------------------------------------------- E31: ドラッグで作る

    [Fact]
    public void FromDrag_ToBottomRight()
    {
        Assert.Equal(new PixelRect(100, 50, 200, 120), CropFrameCalculator.FromDrag(100, 50, 300, 170, Rules(600, 400)));
    }

    [Fact]
    public void FromDrag_ToTopLeft_HasPositiveSize()
    {
        Assert.Equal(new PixelRect(100, 50, 200, 120), CropFrameCalculator.FromDrag(300, 170, 100, 50, Rules(600, 400)));
        // 右上・左下へも
        Assert.Equal(new PixelRect(100, 50, 200, 120), CropFrameCalculator.FromDrag(100, 170, 300, 50, Rules(600, 400)));
        Assert.Equal(new PixelRect(100, 50, 200, 120), CropFrameCalculator.FromDrag(300, 50, 100, 170, Rules(600, 400)));
    }

    [Fact]
    public void FromDrag_OutsideImage_StopsAtEdge()
    {
        var rules = Rules(600, 400);

        Assert.Equal(new PixelRect(100, 50, 500, 350), CropFrameCalculator.FromDrag(100, 50, 5000, 5000, rules));
        Assert.Equal(new PixelRect(0, 0, 100, 50), CropFrameCalculator.FromDrag(100, 50, -5000, -5000, rules));
        // 始点が画像の外でも、画像の内側に収める
        Assert.Equal(new PixelRect(0, 0, 600, 400), CropFrameCalculator.FromDrag(-50, -50, 700, 700, rules));
    }

    [Fact]
    public void FromDrag_FixedRatio_KeepsRatioAndStartCorner()
    {
        var square = Rules(600, 400, new AspectRatio(1, 1));

        // 横長にドラッグ（幅 200・高さ 50）すると、大きくなる方（幅）に合わせた正方形
        var rect = CropFrameCalculator.FromDrag(100, 50, 300, 100, square);
        Assert.Equal(new PixelRect(100, 50, 200, 200), rect);

        // 左上へドラッグしても、始点（右下の角）は動かない
        var up = CropFrameCalculator.FromDrag(300, 300, 250, 100, square);
        Assert.Equal((300, 300), (up.X + up.Width, up.Y + up.Height));
        Assert.Equal(up.Width, up.Height);

        // 画像の端で止まる（下に 100 px しか無いので 100×100）
        var edge = CropFrameCalculator.FromDrag(100, 300, 5000, 5000, square);
        Assert.Equal(new PixelRect(100, 300, 100, 100), edge);
    }

    [Fact]
    public void FromDrag_RoundsStartToPixelBoundary()
    {
        // 始点 (100.4, 49.6) は (100, 50) に、終点は四捨五入して大きさにする
        Assert.Equal(new PixelRect(100, 50, 20, 10), CropFrameCalculator.FromDrag(100.4, 49.6, 120.2, 59.8, Rules(600, 400)));
    }

    [Fact]
    public void FromDrag_NoMovement_IsMinimumRect()
    {
        Assert.Equal(new PixelRect(100, 50, 1, 1), CropFrameCalculator.FromDrag(100, 50, 100, 50, Rules(600, 400)));
        // 右下の角で始めても、画像の内側に収める
        Assert.Equal(new PixelRect(599, 399, 1, 1), CropFrameCalculator.FromDrag(600, 400, 600, 400, Rules(600, 400)));
    }

    [Fact]
    public void FromDrag_NotFinite_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CropFrameCalculator.FromDrag(double.NaN, 0, 10, 10, Rules(600, 400)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CropFrameCalculator.FromDrag(0, 0, double.PositiveInfinity, 10, Rules(600, 400)));
    }

    // ---------------------------------------------------------------- E17: 乱数

    [Fact]
    public void RandomInputs_RectsAreInsideImageWithinMinMaxAndKeepRatio()
    {
        var random = new Random(20261116);
        for (int i = 0; i < 1000; i++)
        {
            int width = CropFrameRulesTests.RandomLength(random), height = CropFrameRulesTests.RandomLength(random);
            AspectRatio? aspect = random.Next(3) switch
            {
                0 => null,
                1 => new AspectRatio(width, height),
                _ => new AspectRatio(random.Next(1, 21), random.Next(1, 21)),
            };
            int minLength = random.Next(2) == 0 ? 1 : random.Next(1, 40);
            var rules = Rules(width, height, aspect, minLength);
            string context = $"#{i}: 画像 {width}×{height}・{aspect}・最小 {minLength} → {rules}";

            Assert.True(rules.MinWidth >= 1 && rules.MinHeight >= 1, context);
            Assert.True(rules.MinWidth <= rules.MaxWidth && rules.MinHeight <= rules.MaxHeight, context);
            Assert.True(rules.MaxWidth <= width && rules.MaxHeight <= height, context);

            double Point(int length) => random.NextDouble() * (length * 1.4) - length * 0.2;
            var dragged = CropFrameCalculator.FromDrag(Point(width), Point(height), Point(width), Point(height), rules);
            CropFrameRulesTests.AssertFrame(dragged, rules, context + $"・FromDrag → {dragged}");

            var moved = CropFrameCalculator.Move(dragged, random.Next(-30000, 30000), random.Next(-30000, 30000), rules);
            CropFrameRulesTests.AssertFrame(moved, rules, context + $"・Move → {moved}");
            Assert.Equal((dragged.Width, dragged.Height), (moved.Width, moved.Height));

            var handle = (FrameHandle)random.Next(8);
            var resized = CropFrameCalculator.ResizeFromHandle(moved, handle, Point(width), Point(height), rules);
            CropFrameRulesTests.AssertFrame(resized, rules, context + $"・ResizeFromHandle({handle}) → {resized}");

            var centered = CropFrameCalculator.ResizeAroundCenter(resized, random.Next(-10, width * 2), rules);
            CropFrameRulesTests.AssertFrame(centered, rules, context + $"・ResizeAroundCenter → {centered}");
        }
    }
}
