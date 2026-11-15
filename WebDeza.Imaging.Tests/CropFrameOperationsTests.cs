using WebDeza.Imaging.Editing;

namespace WebDeza.Imaging.Tests;

/// <summary>CropFrameCalculator の枠の操作（Move・ResizeAroundCenter・ResizeFromHandle・Mirror）</summary>
public class CropFrameOperationsTests
{
    private static readonly OutputSize Cover = OutputSize.ExactSize(1024, 768);

    /// <summary>6000×2000 の画像に 1024×768（最大 2667×2000、最小 128×96）</summary>
    private static readonly FrameRules CoverRules = CropFrameCalculator.Rules(6000, 2000, CropAspect.Free, Cover);

    /// <summary>600×400 の画像で比率が自由（最小 16×16）</summary>
    private static readonly FrameRules FreeRules = CropFrameCalculator.Rules(600, 400, CropAspect.Free, OutputSize.OriginalSize);

    // ---------------------------------------------------------------- E19: Move

    [Theory]
    [InlineData(2667, 1, 3)]    // 1 ÷ (1024 ÷ 2667) = 2.60 → 3
    [InlineData(2667, 10, 26)]  // 26.04 → 26
    [InlineData(300, 1, 1)]     // 0.29 → 最低 1
    [InlineData(300, 10, 3)]    // 2.93 → 3
    [InlineData(1024, 1, 1)]    // 等倍
    public void Move_ConvertsOutputPixelsToImagePixels(int frameWidth, int outputStep, int expectedPixels)
    {
        var rect = CropFrameCalculator.Center(new PixelRect(0, 0, frameWidth, 0), CoverRules);

        var right = CropFrameCalculator.Move(rect, outputStep, 0, CoverRules, Cover);
        var left = CropFrameCalculator.Move(rect, -outputStep, 0, CoverRules, Cover);
        var down = CropFrameCalculator.Move(rect, 0, outputStep, CoverRules, Cover);

        Assert.Equal(rect.X + expectedPixels, right.X);
        Assert.Equal(rect.X - expectedPixels, left.X);
        // 縦も横と同じ倍率で動かす（枠が画像の高さいっぱいのときは動けない）
        Assert.Equal(Math.Min(rect.Y + expectedPixels, 2000 - rect.Height), down.Y);
        Assert.Equal((rect.Width, rect.Height), (right.Width, right.Height));
    }

    [Fact]
    public void Move_StopsAtImageEdge()
    {
        var rect = new PixelRect(3330, 0, 2667, 2000); // 右端まで 3 px

        Assert.Equal(3333, CropFrameCalculator.Move(rect, 10, 0, CoverRules, Cover).X);
        Assert.Equal(0, CropFrameCalculator.Move(rect with { X = 2 }, -1, 0, CoverRules, Cover).X);
        Assert.Equal(0, CropFrameCalculator.Move(rect, 0, -10, CoverRules, Cover).Y);
    }

    [Fact]
    public void Move_Zero_DoesNotMove()
    {
        var rect = new PixelRect(1000, 500, 1200, 900);

        Assert.Equal(rect, CropFrameCalculator.Move(rect, 0, 0, CoverRules, Cover));
    }

    [Fact]
    public void Move_LongSideAndOriginal_UseTheirOutputScale()
    {
        var rules = CropFrameCalculator.Rules(6000, 4000, CropAspect.Free, OutputSize.OriginalSize);
        var rect = new PixelRect(1000, 1000, 3000, 2000);

        // 長辺 1000: 書き出し倍率 = 1000 ÷ 3000。1 px は 3 px
        Assert.Equal(1003, CropFrameCalculator.Move(rect, 1, 0, rules, OutputSize.LongSideOf(1000)).X);
        // 指定しない: 倍率 1
        Assert.Equal(1001, CropFrameCalculator.Move(rect, 1, 0, rules, OutputSize.OriginalSize).X);
    }

    [Fact]
    public void OutputScale_IsOutputWidthOverFrameWidth()
    {
        Assert.Equal(1024.0 / 2667, CropFrameCalculator.OutputScale(new PixelRect(0, 0, 2667, 2000), Cover), 10);
        Assert.Equal(1024.0 / 300, CropFrameCalculator.OutputScale(new PixelRect(0, 0, 300, 225), Cover), 10); // 拡大（> 1）
        Assert.Equal(1.0, CropFrameCalculator.OutputScale(new PixelRect(0, 0, 300, 225), OutputSize.OriginalSize));
    }

    // ---------------------------------------------------------------- E21: ResizeAroundCenter

    [Fact]
    public void ResizeAroundCenter_InMiddle_KeepsCenter()
    {
        var rect = new PixelRect(2000, 500, 1200, 900); // 中心 (2600, 950)

        var larger = CropFrameCalculator.ResizeAroundCenter(rect, 1260, CoverRules);  // 5% 大きく
        var smaller = CropFrameCalculator.ResizeAroundCenter(rect, 1140, CoverRules); // 5% 小さく

        // 高さの差が奇数（45）のときは、半端の 0.5 px だけ上へ寄る
        Assert.Equal(new PixelRect(1970, 477, 1260, 945), larger);
        Assert.Equal(new PixelRect(2030, 522, 1140, 855), smaller);
        Assert.Equal((2600.0, 949.5), Center(larger));
        Assert.Equal((2600.0, 949.5), Center(smaller));
    }

    [Fact]
    public void ResizeAroundCenter_NearEdge_ShiftsInside()
    {
        var rect = new PixelRect(0, 1100, 1200, 900); // 左端と下端に接している

        var larger = CropFrameCalculator.ResizeAroundCenter(rect, 1600, CoverRules);

        Assert.Equal(new PixelRect(0, 800, 1600, 1200), larger);
    }

    [Fact]
    public void ResizeAroundCenter_ClampsToMinAndMax()
    {
        var rect = new PixelRect(2000, 500, 1200, 900);

        Assert.Equal((2667, 2000), Size(CropFrameCalculator.ResizeAroundCenter(rect, 99999, CoverRules)));
        Assert.Equal((128, 96), Size(CropFrameCalculator.ResizeAroundCenter(rect, 1, CoverRules)));
    }

    [Fact]
    public void ResizeAroundCenter_Free_KeepsFrameAspect()
    {
        var rect = new PixelRect(100, 100, 200, 100);

        Assert.Equal(new PixelRect(90, 95, 220, 110), CropFrameCalculator.ResizeAroundCenter(rect, 220, FreeRules));
    }

    // ---------------------------------------------------------------- E20: ResizeFromHandle

    [Fact]
    public void ResizeFromHandle_FixedRatio_BottomRightOutward_KeepsTopLeftAndStopsAtEdge()
    {
        var rect = new PixelRect(2000, 500, 1200, 900);

        // 少し外へ: 左上を固定したまま大きくなる
        var dragged = CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.BottomRight, 3600, 1500, CoverRules);
        Assert.Equal(new PixelRect(2000, 500, 1600, 1200), dragged);

        // 大きく外へ: 下端（画像の高さ 2000）で止まる。高さ 1500 → 幅 2000
        var atEdge = CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.BottomRight, 9000, 9000, CoverRules);
        Assert.Equal(new PixelRect(2000, 500, 2000, 1500), atEdge);
    }

    [Fact]
    public void ResizeFromHandle_FixedRatio_FollowsLargerDirection()
    {
        var rect = new PixelRect(2000, 500, 1200, 900);

        // 横は 1300、縦は 1000（幅にすると 1333）だけ離れている → 大きくなる方（1333）に合わせる
        var dragged = CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.BottomRight, 3300, 1500, CoverRules);

        Assert.Equal(new PixelRect(2000, 500, 1333, 1000), dragged);
    }

    [Fact]
    public void ResizeFromHandle_FixedRatio_PastOppositeCorner_StopsAtMinWithoutFlipping()
    {
        var rect = new PixelRect(2000, 500, 1200, 900); // 右下 (3200, 1400)

        // 左上のハンドルを、右下の角を越えて右下へ
        var dragged = CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.TopLeft, 5000, 1900, CoverRules);

        // 最小（128×96）で止まり、右下の角は動かない
        Assert.Equal(new PixelRect(3200 - 128, 1400 - 96, 128, 96), dragged);
    }

    [Theory]
    [InlineData(FrameHandle.TopLeft, 1000, 100, 2000, 1500)]   // 左上へ: 右下 (3200, 1400) を固定
    [InlineData(FrameHandle.TopRight, 4000, 100, 2000, 1500)]  // 右上へ: 左下 (2000, 1400) を固定
    [InlineData(FrameHandle.BottomLeft, 1000, 1900, 2000, 1500)] // 左下へ: 右上 (3200, 500) を固定
    public void ResizeFromHandle_FixedRatio_EachCorner_KeepsOppositeCorner(
        FrameHandle handle, double px, double py, int minExpectedWidth, int maxExpectedWidth)
    {
        var rect = new PixelRect(2000, 500, 1200, 900);

        var dragged = CropFrameCalculator.ResizeFromHandle(rect, handle, px, py, CoverRules);

        Assert.InRange(dragged.Width, Math.Min(minExpectedWidth, maxExpectedWidth), Math.Max(minExpectedWidth, maxExpectedWidth));
        switch (handle)
        {
            case FrameHandle.TopLeft:
                Assert.Equal((3200, 1400), (dragged.X + dragged.Width, dragged.Y + dragged.Height));
                break;
            case FrameHandle.TopRight:
                Assert.Equal((2000, 1400), (dragged.X, dragged.Y + dragged.Height));
                break;
            case FrameHandle.BottomLeft:
                Assert.Equal((3200, 500), (dragged.X + dragged.Width, dragged.Y));
                break;
        }
        CropFrameRulesTests.AssertFrame(dragged, CoverRules, handle.ToString());
    }

    [Fact]
    public void ResizeFromHandle_Free_RightEdge_ChangesOnlyWidth()
    {
        var rect = new PixelRect(100, 100, 200, 100);

        var dragged = CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.Right, 450.4, 999, FreeRules);

        Assert.Equal(new PixelRect(100, 100, 350, 100), dragged);
    }

    [Fact]
    public void ResizeFromHandle_Free_EdgesAndCorners()
    {
        var rect = new PixelRect(100, 100, 200, 100); // 右下 (300, 200)

        Assert.Equal(new PixelRect(100, 40, 200, 160), CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.Top, -5, 40, FreeRules));
        Assert.Equal(new PixelRect(100, 100, 200, 300), CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.Bottom, 0, 999, FreeRules)); // 下端で止まる
        Assert.Equal(new PixelRect(0, 100, 300, 100), CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.Left, -50, 0, FreeRules));    // 左端で止まる
        Assert.Equal(new PixelRect(50, 150, 250, 50), CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.TopLeft, 50, 150, FreeRules));
        // 反対側を越えても裏返らず、最小（16）で止まる
        Assert.Equal(new PixelRect(284, 100, 16, 100), CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.Left, 500, 0, FreeRules));
        Assert.Equal(new PixelRect(100, 100, 16, 16), CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.BottomRight, 0, 0, FreeRules));
    }

    [Fact]
    public void ResizeFromHandle_FixedRatio_Edge_KeepsOtherAxisCenter()
    {
        // 比率が決まっているときに辺のハンドルが来ても、比率を保ち、もう一方の向きの中心を保つ
        var rect = new PixelRect(2000, 500, 1200, 900); // 縦の中心 950

        var dragged = CropFrameCalculator.ResizeFromHandle(rect, FrameHandle.Right, 3600, 0, CoverRules);

        Assert.Equal(new PixelRect(2000, 350, 1600, 1200), dragged);
    }

    [Fact]
    public void ResizeFromHandle_NonFinitePointer_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CropFrameCalculator.ResizeFromHandle(new PixelRect(0, 0, 100, 100), FrameHandle.Right, double.NaN, 0, FreeRules));
    }

    // ---------------------------------------------------------------- E21: Mirror

    [Fact]
    public void Mirror_Horizontal_MovesX()
    {
        // X' = 6000 − 1000 − 2667 = 2333
        Assert.Equal(new PixelRect(2333, 0, 2667, 2000),
            CropFrameCalculator.Mirror(new PixelRect(1000, 0, 2667, 2000), horizontal: true, CoverRules));
    }

    [Fact]
    public void Mirror_Vertical_MovesY()
    {
        // Y' = 2000 − 300 − 900 = 800
        Assert.Equal(new PixelRect(1000, 800, 1200, 900),
            CropFrameCalculator.Mirror(new PixelRect(1000, 300, 1200, 900), horizontal: false, CoverRules));
    }

    [Fact]
    public void Mirror_Twice_ReturnsOriginal()
    {
        var rect = new PixelRect(123, 45, 1200, 900);

        foreach (bool horizontal in new[] { true, false })
        {
            var once = CropFrameCalculator.Mirror(rect, horizontal, CoverRules);
            Assert.Equal(rect, CropFrameCalculator.Mirror(once, horizontal, CoverRules));
        }
    }

    // ---------------------------------------------------------------- E17 を操作した後の枠に広げる

    [Fact]
    public void RandomOperations_FramesStayValid()
    {
        var random = new Random(20261116);
        var handles = Enum.GetValues<FrameHandle>();

        for (int i = 0; i < 1000; i++)
        {
            int width = CropFrameRulesTests.RandomLength(random), height = CropFrameRulesTests.RandomLength(random);
            var aspect = CropFrameRulesTests.RandomAspect(random);
            var size = CropFrameRulesTests.RandomOutputSize(random);
            var rules = CropFrameCalculator.Rules(width, height, aspect, size);
            var rect = CropFrameCalculator.Initial(rules);
            string context = $"#{i}: 画像 {width}×{height}・{aspect}・{size}";

            for (int step = 0; step < 10; step++)
            {
                string operation;
                switch (random.Next(5))
                {
                    case 0:
                        int dx = random.Next(-20, 21), dy = random.Next(-20, 21);
                        operation = $"Move({dx}, {dy})";
                        rect = CropFrameCalculator.Move(rect, dx, dy, rules, size);
                        break;
                    case 1:
                        int newWidth = (int)(rect.Width * (random.NextDouble() * 1.6 + 0.2));
                        operation = $"ResizeAroundCenter({newWidth})";
                        rect = CropFrameCalculator.ResizeAroundCenter(rect, newWidth, rules);
                        break;
                    case 2:
                        var handle = handles[random.Next(handles.Length)];
                        double px = random.NextDouble() * width * 1.4 - width * 0.2;
                        double py = random.NextDouble() * height * 1.4 - height * 0.2;
                        operation = $"ResizeFromHandle({handle}, {px:F1}, {py:F1})";
                        rect = CropFrameCalculator.ResizeFromHandle(rect, handle, px, py, rules);
                        break;
                    case 3:
                        bool horizontal = random.Next(2) == 0;
                        operation = $"Mirror({horizontal})";
                        rect = CropFrameCalculator.Mirror(rect, horizontal, rules);
                        break;
                    default:
                        operation = "Center";
                        rect = CropFrameCalculator.Center(rect, rules);
                        break;
                }

                CropFrameRulesTests.AssertFrame(rect, rules, $"{context}・{step + 1} 回目 {operation}");
            }
        }
    }

    [Fact]
    public void ResizeFromHandle_RandomCornerDrags_KeepOppositeCorner()
    {
        // 比率が決まっていても、角のドラッグでは反対側の角が動かない
        var random = new Random(7);
        var corners = new[] { FrameHandle.TopLeft, FrameHandle.TopRight, FrameHandle.BottomRight, FrameHandle.BottomLeft };

        for (int i = 0; i < 500; i++)
        {
            var aspect = random.Next(2) == 0 ? CropAspect.Free : CropAspect.Fixed(random.Next(1, 17), random.Next(1, 17));
            var rules = CropFrameCalculator.Rules(4000, 3000, aspect, OutputSize.OriginalSize);
            var start = CropFrameCalculator.Clamp(new PixelRect(random.Next(4000), random.Next(3000), random.Next(16, 2000), random.Next(16, 2000)), rules);
            var handle = corners[random.Next(corners.Length)];

            var dragged = CropFrameCalculator.ResizeFromHandle(start, handle, random.Next(-500, 4500), random.Next(-500, 3500), rules);

            (int, int) Opposite(PixelRect r) => handle switch
            {
                FrameHandle.TopLeft => (r.X + r.Width, r.Y + r.Height),
                FrameHandle.TopRight => (r.X, r.Y + r.Height),
                FrameHandle.BottomRight => (r.X, r.Y),
                _ => (r.X + r.Width, r.Y),
            };
            Assert.True(Opposite(start) == Opposite(dragged), $"#{i}: {aspect}・{handle}: {start} → {dragged}");
        }
    }

    private static (double, double) Center(PixelRect r) => (r.X + r.Width / 2.0, r.Y + r.Height / 2.0);

    private static (int, int) Size(PixelRect r) => (r.Width, r.Height);
}
