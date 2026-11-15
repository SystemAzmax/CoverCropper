using System.Windows;
using WebDeza.Models;
using WebDeza.Views;

namespace WebDeza.Tests.Views;

public class WindowPlacementTests
{
    // 1920×1080 のモニターが 2 枚（左右に並ぶ）
    private static readonly Rect Screen = new(0, 0, 3840, 1080);

    private static AppSettings Saved(double left, double top, double width, double height, bool maximized = false) => new()
    {
        WindowLeft = left, WindowTop = top, WindowWidth = width, WindowHeight = height, WindowMaximized = maximized,
    };

    [Fact]
    public void GetRestoreBounds_SavedInsideScreen_ReturnsSavedBounds()
    {
        Assert.Equal(new Rect(2000, 100, 1440, 900), WindowPlacement.GetRestoreBounds(Saved(2000, 100, 1440, 900), Screen, 1024, 640));
    }

    [Fact]
    public void GetRestoreBounds_SmallerThanMinimum_IsEnlarged()
    {
        Assert.Equal(new Rect(10, 10, 1024, 640), WindowPlacement.GetRestoreBounds(Saved(10, 10, 300, 200), Screen, 1024, 640));
    }

    [Theory]
    [InlineData(4000, 100)]   // 右のモニターが外された
    [InlineData(-2000, 100)]
    [InlineData(100, 1075)]   // タイトルバーが画面の下にはみ出る
    [InlineData(3800, 100)]   // タイトルバーの左寄りの点が画面の外
    public void GetRestoreBounds_OutsideScreen_ReturnsNull(double left, double top)
    {
        Assert.Null(WindowPlacement.GetRestoreBounds(Saved(left, top, 1440, 900), Screen, 1024, 640));
    }

    [Fact]
    public void GetRestoreBounds_NotSaved_ReturnsNull()
    {
        Assert.Null(WindowPlacement.GetRestoreBounds(new AppSettings(), Screen, 1024, 640));
        Assert.Null(WindowPlacement.GetRestoreBounds(new AppSettings { WindowLeft = 0, WindowTop = 0, WindowWidth = 800 }, Screen, 1024, 640));
    }

    [Fact]
    public void Save_Normal_WritesBoundsAndNotMaximized()
    {
        var settings = new AppSettings { WindowMaximized = true };

        WindowPlacement.Save(settings, WindowState.Normal, new Rect(30, 40, 1500, 950));

        Assert.Equal((30.0, 40.0, 1500.0, 950.0, false),
            (settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight, settings.WindowMaximized));
    }

    [Fact]
    public void Save_Maximized_WritesRestoreBoundsAndMaximized()
    {
        var settings = new AppSettings();

        WindowPlacement.Save(settings, WindowState.Maximized, new Rect(30, 40, 1500, 950));

        Assert.Equal((30.0, 1500.0, true), (settings.WindowLeft, settings.WindowWidth, settings.WindowMaximized));
        // 保存した値で、最大化の前の位置に戻せる
        Assert.Equal(new Rect(30, 40, 1500, 950), WindowPlacement.GetRestoreBounds(settings, Screen, 1024, 640));
    }

    [Fact]
    public void Save_EmptyBounds_KeepsPreviousBounds()
    {
        var settings = Saved(30, 40, 1500, 950);

        WindowPlacement.Save(settings, WindowState.Minimized, Rect.Empty);

        Assert.Equal((30.0, 1500.0, false), (settings.WindowLeft, settings.WindowWidth, settings.WindowMaximized));
    }
}
