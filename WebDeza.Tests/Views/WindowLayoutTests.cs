using System.Windows;
using WebDeza.Views.Workspace;

namespace WebDeza.Tests.Views;

/// <summary>子ウィンドウの配置の計算（設計書 F-02。V40 のうち「内側へ寄せる」と、移動・大きさの変更・新しい大きさ。実装計画書 7-2）</summary>
public class WindowLayoutTests
{
    private const double TitleBar = 30;
    private static readonly Size Workspace = new(1000, 600);

    // ---------------------------------------------------------------- V40: 内側へ寄せる

    [Fact]
    public void KeepInside_TitleBarInside_IsUnchanged()
    {
        // 下の方はワークスペースの外に出ていても、タイトルバーが内側なら動かさない
        var window = new Rect(100, 500, 400, 300);

        Assert.Equal(window, WindowLayout.KeepInside(window, Workspace, TitleBar));
    }

    [Theory]
    [InlineData(800, 100, 600, 100)]    // 右に出た → 右端をそろえる
    [InlineData(-50, 100, 0, 100)]      // 左に出た
    [InlineData(100, -20, 100, 0)]      // 上に出た
    [InlineData(100, 590, 100, 570)]    // 下に出た → タイトルバーの高さだけ見えるようにする
    [InlineData(900, 700, 600, 570)]    // 右下の外
    public void KeepInside_TitleBarOutside_MovesInside(double x, double y, double expectedX, double expectedY)
    {
        var result = WindowLayout.KeepInside(new Rect(x, y, 400, 300), Workspace, TitleBar);

        Assert.Equal(new Rect(expectedX, expectedY, 400, 300), result);
        Assert.True(WindowLayout.IsTitleBarInside(result, Workspace, TitleBar));
    }

    [Fact]
    public void KeepInside_WorkspaceShrank_WindowWiderThanWorkspaceIsNarrowed()
    {
        // ワークスペースが 500 幅に縮んだ: 幅 800 の子ウィンドウは 500 に縮め、左端に寄せる（高さは変えない）
        var result = WindowLayout.KeepInside(new Rect(100, 50, 800, 400), new Size(500, 600), TitleBar);

        Assert.Equal(new Rect(0, 50, 500, 400), result);
    }

    [Fact]
    public void KeepInside_WorkspaceNarrowerThanMinimum_KeepsMinimumWidthAtLeft()
    {
        var result = WindowLayout.KeepInside(new Rect(50, 0, 300, 200), new Size(100, 600), TitleBar);

        Assert.Equal(new Rect(0, 0, WindowLayout.MinWidth, 200), result);
    }

    // ---------------------------------------------------------------- 移動

    [Fact]
    public void Move_AddsDeltaFromStart()
    {
        var result = WindowLayout.Move(new Rect(100, 100, 400, 300), new Vector(50, -30), Workspace, TitleBar);

        Assert.Equal(new Rect(150, 70, 400, 300), result);
    }

    [Theory]
    [InlineData(-500, 0, 0, 100)]
    [InlineData(1000, 0, 600, 100)]
    [InlineData(0, -500, 100, 0)]
    [InlineData(0, 1000, 100, 570)]
    public void Move_StopsWhereTitleBarStaysInside(double dx, double dy, double expectedX, double expectedY)
    {
        var result = WindowLayout.Move(new Rect(100, 100, 400, 300), new Vector(dx, dy), Workspace, TitleBar);

        Assert.Equal(new Rect(expectedX, expectedY, 400, 300), result);
    }

    // ---------------------------------------------------------------- 大きさの変更

    [Fact]
    public void Resize_RightBottom_MovesOnlyThoseEdges()
    {
        var result = WindowLayout.Resize(new Rect(100, 100, 400, 300), ResizeEdges.BottomRight, new Vector(60, 40), Workspace, TitleBar);

        Assert.Equal(new Rect(100, 100, 460, 340), result);
    }

    [Fact]
    public void Resize_TopLeft_KeepsOppositeEdges()
    {
        var result = WindowLayout.Resize(new Rect(100, 100, 400, 300), ResizeEdges.TopLeft, new Vector(-40, 30), Workspace, TitleBar);

        Assert.Equal(new Rect(60, 130, 440, 270), result);
    }

    [Fact]
    public void Resize_NotSmallerThanMinimum()
    {
        // 左の辺を右の辺より右まで引いても、最小の幅で止まり、右の辺は動かない
        var left = WindowLayout.Resize(new Rect(100, 100, 400, 300), ResizeEdges.Left, new Vector(1000, 0), Workspace, TitleBar);
        var bottom = WindowLayout.Resize(new Rect(100, 100, 400, 300), ResizeEdges.Bottom, new Vector(0, -1000), Workspace, TitleBar);

        Assert.Equal(new Rect(500 - WindowLayout.MinWidth, 100, WindowLayout.MinWidth, 300), left);
        Assert.Equal(new Rect(100, 100, 400, WindowLayout.MinHeight), bottom);
    }

    [Fact]
    public void Resize_Top_TitleBarDoesNotGoBelowWorkspace()
    {
        // 下側がワークスペースの外にはみ出した子ウィンドウ（下の辺は 800、ワークスペースの高さは 600）を、上の辺で縮める。
        // 上の辺はタイトルバーが見える所（600 - 30）で止まる
        var result = WindowLayout.Resize(new Rect(100, 500, 400, 300), ResizeEdges.Top, new Vector(0, 200), Workspace, TitleBar);

        Assert.Equal(new Rect(100, 570, 400, 230), result);
        Assert.True(WindowLayout.IsTitleBarInside(result, Workspace, TitleBar));
    }

    [Fact]
    public void Resize_TopLeft_TitleBarDoesNotGoBelowWorkspace()
    {
        var result = WindowLayout.Resize(new Rect(100, 500, 400, 300), ResizeEdges.TopLeft, new Vector(50, 1000), Workspace, TitleBar);

        Assert.Equal(new Rect(150, 570, 350, 230), result);
    }

    [Fact]
    public void Resize_NotBeyondWorkspace()
    {
        var topLeft = WindowLayout.Resize(new Rect(100, 100, 400, 300), ResizeEdges.TopLeft, new Vector(-500, -500), Workspace, TitleBar);
        var bottomRight = WindowLayout.Resize(new Rect(100, 100, 400, 300), ResizeEdges.BottomRight, new Vector(2000, 2000), Workspace, TitleBar);

        Assert.Equal(new Rect(0, 0, 500, 400), topLeft);
        Assert.Equal(new Rect(100, 100, 900, 500), bottomRight);
    }

    // ---------------------------------------------------------------- V40: 新しく開く位置

    [Fact]
    public void NewWindowPosition_FiveTimes_ShiftsAndReturnsToTopLeftAtEdge()
    {
        // ワークスペース 500×400 に 400×300 の子ウィンドウ: 0, 30, 60, 90 と右下へずれ、120 では下にはみ出すので左上に戻る
        var workspace = new Size(500, 400);
        var size = new Size(400, 300);
        Rect? previous = null;
        var positions = new List<Point>();
        for (int i = 0; i < 5; i++)
        {
            var position = WindowLayout.NewWindowPosition(previous, size, workspace);
            positions.Add(position);
            previous = new Rect(position, size);
        }

        Assert.Equal(new[] { new Point(0, 0), new Point(30, 30), new Point(60, 60), new Point(90, 90), new Point(0, 0) }, positions);
    }

    [Fact]
    public void NewWindowPosition_FollowsMovedPreviousWindow()
    {
        // 前の子ウィンドウを利用者が動かしていたら、そこからずらす
        var position = WindowLayout.NewWindowPosition(new Rect(200, 100, 300, 200), new Size(300, 200), Workspace);

        Assert.Equal(new Point(230, 130), position);
    }

    [Fact]
    public void NewWindowPosition_TooLargeToShift_TopLeft()
    {
        var position = WindowLayout.NewWindowPosition(new Rect(0, 0, 990, 590), new Size(990, 590), Workspace);

        Assert.Equal(new Point(0, 0), position);
    }

    // ---------------------------------------------------------------- 新しく開いたときの大きさ

    private static readonly Size Chrome = new(26, 56);

    [Fact]
    public void NewWindowSize_SmallImage_FitsAt100Percent()
    {
        var size = WindowLayout.NewWindowSize(Workspace, 300, 200, dpiScale: 1, Chrome);

        Assert.Equal(new Size(326, 256), size);
    }

    [Fact]
    public void NewWindowSize_HighDpi_100PercentIsOnePhysicalPixel()
    {
        // 150% の環境では、300 px は 200 DIP
        var size = WindowLayout.NewWindowSize(Workspace, 300, 150, dpiScale: 1.5, Chrome);

        Assert.Equal(new Size(226, 156), size);
    }

    [Fact]
    public void NewWindowSize_LargeImage_LimitedTo80PercentKeepingAspect()
    {
        // ワークスペース 1000×600 の 80% は 800×480。画像を描ける所は 774×424 → 幅で決まる（倍率 774/4000）
        var wide = WindowLayout.NewWindowSize(Workspace, 4000, 2000, dpiScale: 1, Chrome);
        // 縦長は高さで決まる（倍率 424/4000）
        var tall = WindowLayout.NewWindowSize(Workspace, 2000, 4000, dpiScale: 1, Chrome);

        Assert.Equal(new Size(800, 387 + 56), wide);
        Assert.Equal(new Size(212 + 26, 480), tall);
    }

    [Fact]
    public void NewWindowSize_TinyImage_NotSmallerThanMinimum()
    {
        var size = WindowLayout.NewWindowSize(Workspace, 16, 16, dpiScale: 1, Chrome);

        Assert.Equal(new Size(WindowLayout.MinWidth, WindowLayout.MinHeight), size);
    }
}
