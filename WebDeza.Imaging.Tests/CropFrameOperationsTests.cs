using WebDeza.Imaging.Editing;

namespace WebDeza.Imaging.Tests;

/// <summary>CropFrameCalculator の矩形の操作（Move・ResizeAroundCenter・FitToAspect・ResizeFromHandle・Mirror）</summary>
public class CropFrameOperationsTests
{
    /// <summary>6000×2000 の画像で 4:3・最小の長さ 96（最大 2667×2000、最小 128×96）</summary>
    private static readonly FrameRules CoverRules = CropFrameRulesTests.Rules4x3;

    /// <summary>600×400 の画像で比率なし・最小の長さ 16（最小 16×16）</summary>
    private static readonly FrameRules FreeRules = CropFrameCalculator.Rules(600, 400, null, 16);

    // ---------------------------------------------------------------- E19: Move（px 単位）

    [Fact]
    public void Move_ByPixels_StopsAtImageEdge()
    {
        var rect = new PixelRect(3330, 0, 2667, 2000); // 右端まで 3 px

        Assert.Equal(3333, CropFrameCalculator.Move(rect, 10, 0, CoverRules).X);
        Assert.Equal(1, CropFrameCalculator.Move(rect with { X = 2 }, -1, 0, CoverRules).X);
        Assert.Equal(0, CropFrameCalculator.Move(rect, 0, -10, CoverRules).Y);
    }

    [Fact]
    public void Move_Zero_DoesNotMove()
    {
        var rect = new PixelRect(1000, 500, 1200, 900);

        Assert.Equal(rect, CropFrameCalculator.Move(rect, 0, 0, CoverRules));
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

    // ---------------------------------------------------------------- FitToAspect（比率を変えたときの合わせ直し。webDeza 実装計画書 8-8）

    private static FrameRules Rules600x400(int width, int height) => CropFrameCalculator.Rules(600, 400, new AspectRatio(width, height));

    [Fact]
    public void FitToAspect_KeepsCenterAndArea()
    {
        // 200×50（面積 10000、中心 (200, 125)）を 1:1 に → 100×100
        Assert.Equal(new PixelRect(150, 75, 100, 100),
            CropFrameCalculator.FitToAspect(new PixelRect(100, 100, 200, 50), Rules600x400(1, 1)));
    }

    [Fact]
    public void FitToAspect_SwapTwice_ReturnsOriginal()
    {
        // 縦横を入れ替えると幅と高さが入れ替わり、戻せば元の枠になる（縮まない）
        var rect = new PixelRect(100, 50, 400, 300);

        var portrait = CropFrameCalculator.FitToAspect(rect, Rules600x400(3, 4));
        Assert.Equal(new PixelRect(150, 0, 300, 400), portrait);
        Assert.Equal(rect, CropFrameCalculator.FitToAspect(portrait, Rules600x400(4, 3)));
    }

    [Fact]
    public void FitToAspect_TooLarge_Shrinks_AndNearEdge_ShiftsInside()
    {
        // 画像全体（600×400）を 1:1 に: 面積どおりなら 490 角だが、画像に収まる 400 角にする
        Assert.Equal(new PixelRect(100, 0, 400, 400),
            CropFrameCalculator.FitToAspect(new PixelRect(0, 0, 600, 400), Rules600x400(1, 1)));

        // 左上の 100×100 を 16:9 に: 133×75。左へはみ出す分は内側へずらす
        Assert.Equal(new PixelRect(0, 12, 133, 75),
            CropFrameCalculator.FitToAspect(new PixelRect(0, 0, 100, 100), Rules600x400(16, 9)));
    }

    [Fact]
    public void FitToAspect_Free_KeepsRect()
    {
        var rect = new PixelRect(100, 100, 200, 50);

        Assert.Equal(rect, CropFrameCalculator.FitToAspect(rect, FreeRules));
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
            var aspect = CropFrameRulesTests.RandomAspect(random, width, height);
            int minLength = random.Next(2) == 0 ? 1 : random.Next(1, 200);
            var rules = CropFrameCalculator.Rules(width, height, aspect, minLength);
            var rect = CropFrameCalculator.Initial(rules);
            string context = $"#{i}: 画像 {width}×{height}・{aspect}・最小 {minLength}";

            for (int step = 0; step < 10; step++)
            {
                string operation;
                switch (random.Next(5))
                {
                    case 0:
                        int dx = random.Next(-20, 21), dy = random.Next(-20, 21);
                        operation = $"Move({dx}, {dy})";
                        rect = CropFrameCalculator.Move(rect, dx, dy, rules);
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
            AspectRatio? aspect = random.Next(2) == 0 ? null : new AspectRatio(random.Next(1, 17), random.Next(1, 17));
            var rules = CropFrameCalculator.Rules(4000, 3000, aspect, 16);
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
