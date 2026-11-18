using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.InfrastructureTests;

/// <summary>マウスの歯止め: 画面の端から 200 px 以内には動かさない（画面を動かさないので、普段のテストで実行する）</summary>
public class ScreenGuardTests
{
    private static readonly Rectangle[] Single = [new(0, 0, 1920, 1080)];

    // 利用者の PC と同じ並び（主モニター 3840×2160 の左に 2 段、右上に 1 枚。右上のモニターの下は空き）
    private static readonly Rectangle[] Layout =
    [
        new(0, 0, 3840, 2160),
        new(-1920, 1080, 1920, 1080),
        new(3840, 0, 1920, 1080),
        new(-1920, 0, 1920, 1080),
    ];

    [Theory]
    [InlineData(960, 540)]
    [InlineData(200, 200)]   // 左上の端からちょうど 200 px
    [InlineData(1719, 879)]  // 右下の端（1919, 1079）からちょうど 200 px
    public void IsSafe_200PxOrMoreFromEdges_True(int x, int y)
    {
        Assert.True(ScreenGuard.IsSafe(new Point(x, y), Single));
    }

    [Theory]
    [InlineData(199, 540)]
    [InlineData(960, 199)]
    [InlineData(1720, 540)]
    [InlineData(960, 880)]
    [InlineData(0, 0)]
    [InlineData(-500, 540)]  // モニターの外
    public void IsSafe_Within200PxOfEdges_False(int x, int y)
    {
        Assert.False(ScreenGuard.IsSafe(new Point(x, y), Single));
    }

    [Fact]
    public void IsSafe_NearBorderBetweenMonitors_True()
    {
        // モニターどうしの境は画面の端ではない
        Assert.True(ScreenGuard.IsSafe(new Point(0, 1000), Layout));
        Assert.True(ScreenGuard.IsSafe(new Point(3840, 500), Layout));
        Assert.True(ScreenGuard.IsSafe(new Point(-1000, 1080), Layout));
    }

    [Fact]
    public void IsSafe_NearEdgeFacingNoMonitor_False()
    {
        // 右上のモニターの下は空き。仮想画面の長方形の中でも、モニターの無い所との境は画面の端として扱う
        Assert.True(ScreenGuard.IsSafe(new Point(4800, 800), Layout));
        Assert.False(ScreenGuard.IsSafe(new Point(4800, 1000), Layout));
        Assert.False(ScreenGuard.IsSafe(new Point(4800, 1500), Layout));
        // 主モニターの右下の角の近く（右は空き）
        Assert.False(ScreenGuard.IsSafe(new Point(3700, 1500), Layout));
    }

    [Fact]
    public void EnsureSafe_Unsafe_Throws()
    {
        var ex = Assert.Throws<UnsafePointerPositionException>(() => ScreenGuard.EnsureSafe(new Point(10, 10), Single));
        Assert.Contains("(10, 10)", ex.Message);
        ScreenGuard.EnsureSafe(new Point(960, 540), Single);
    }

    [Fact]
    public void Pointer_OutsideScreens_ThrowsBeforeMoving()
    {
        // 歯止めは、マウスを動かす前に確かめる（ここでは例外になるので、マウスは動かない）
        Assert.Throws<UnsafePointerPositionException>(() => Pointer.MoveTo(new Point(-100000, -100000)));
        Assert.Throws<UnsafePointerPositionException>(() => Pointer.Click(new Point(-100000, -100000)));
        Assert.Throws<UnsafePointerPositionException>(() => Pointer.Wheel(new Point(-100000, -100000), 1));
    }

    [Fact]
    public void PointerDrag_PathLeavesSafeArea_ThrowsBeforePressing()
    {
        // 始めと終わりが安全でも、途中が画面の外を通るドラッグは、ボタンを押す前に止める
        var monitors = ScreenGuard.Monitors();
        var first = monitors[0];
        var start = Pointer.Center(first);

        Assert.Throws<UnsafePointerPositionException>(() => Pointer.Drag(start, new Point(first.Left - 100000, start.Y)));
    }

    [Fact]
    public void FileDrag_TargetOutsideScreens_ThrowsBeforeShowingSource()
    {
        // ファイルの D&D も、落とす位置が安全でなければ、ドラッグ元のフォームを出す前に止める
        Assert.Throws<UnsafePointerPositionException>(() => FileDrag.Run(["a.jpg"], new Point(-100000, -100000), drop: true));
    }
}
