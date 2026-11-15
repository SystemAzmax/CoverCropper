using System.Windows;
using System.Windows.Input;
using WebDeza.Imaging;
using WebDeza.Views.Canvas;

namespace WebDeza.Tests.Views;

/// <summary>「選択」の道具: 作る・解除（設計書 F-04。V30 のうち「作る」「解除」。実装計画書 8-5）</summary>
public class SelectToolTests
{
    private readonly FakeSession _session = new() { ImageWidth = 400, ImageHeight = 300 };
    private readonly FakeHost _host = new();
    private readonly ICanvasTool _tool;

    public SelectToolTests()
    {
        _tool = new SelectToolFactory().Create(_session, _host);
    }

    /// <summary>画面の座標と画像の座標が同じ（倍率 1・位置 0）として、ポインターを作る</summary>
    private static CanvasPointer At(double x, double y, MouseButton? button = MouseButton.Left)
        => new(new Point(x, y), new Point(x, y), button, ModifierKeys.None);

    private void Drag(double x1, double y1, double x2, double y2)
    {
        _tool.OnPointerDown(At(x1, y1));
        _tool.OnPointerMove(At(x2, y2));
        _tool.OnPointerUp(At(x2, y2));
    }

    [Fact]
    public void Factory_IsSelectWithM()
    {
        var factory = new SelectToolFactory();

        Assert.Equal(("select", "選択", Key.M), (factory.Id, factory.DisplayName, factory.Shortcut));
        Assert.Same(Cursors.Cross, _tool.GetCursor(At(0, 0, null)));
    }

    // ---------------------------------------------------------------- 作る

    [Fact]
    public void Drag_OnEmptyArea_CreatesSelectionBetweenPoints()
    {
        Drag(50, 40, 250, 140);

        Assert.Equal(new PixelRect(50, 40, 200, 100), _session.Selection);
        Assert.False(_host.Captured); // 離したらキャプチャを外す
    }

    [Fact]
    public void Drag_TowardTopLeft_CreatesPositiveRect()
    {
        Drag(250, 140, 50, 40);

        Assert.Equal(new PixelRect(50, 40, 200, 100), _session.Selection);
    }

    [Fact]
    public void Drag_UpdatesSelectionWhileMoving_AndCapturesMouse()
    {
        _tool.OnPointerDown(At(10, 10));
        Assert.True(_host.Captured);

        _tool.OnPointerMove(At(60, 30));
        Assert.Equal(new PixelRect(10, 10, 50, 20), _session.Selection);
        _tool.OnPointerMove(At(110, 90));
        Assert.Equal(new PixelRect(10, 10, 100, 80), _session.Selection);
    }

    [Fact]
    public void Drag_OutsideImage_StopsAtEdges()
    {
        Drag(300, 200, 1000, -500);

        Assert.Equal(new PixelRect(300, 0, 100, 200), _session.Selection);
    }

    [Fact]
    public void Drag_StartingOutsideImage_StartsFromEdge()
    {
        // 画像の外（灰色の所）から始めても、画像の端からの矩形になる
        Drag(-30, -20, 100, 50);

        Assert.Equal(new PixelRect(0, 0, 100, 50), _session.Selection);
    }

    [Fact]
    public void Drag_ReplacesPreviousSelection()
    {
        _session.Selection = new PixelRect(10, 10, 20, 20);

        Drag(100, 100, 200, 180);

        Assert.Equal(new PixelRect(100, 100, 100, 80), _session.Selection);
    }

    [Fact]
    public void Drag_FollowsAspectFromRules()
    {
        // 比率 1:1 のときは正方形（設計書 F-04。比率は 8-8 で選べるようにする）
        _session.Aspect = new AspectRatio(1, 1);

        Drag(10, 10, 210, 60);

        Assert.Equal(new PixelRect(10, 10, 200, 200), _session.Selection);
    }

    // ---------------------------------------------------------------- 解除

    [Fact]
    public void ClickOutsideSelection_Clears()
    {
        _session.Selection = new PixelRect(100, 100, 50, 50);

        _tool.OnPointerDown(At(20, 20));
        _tool.OnPointerUp(At(20, 20));

        Assert.Null(_session.Selection);
    }

    [Fact]
    public void ClickOutsideImage_Clears()
    {
        _session.Selection = new PixelRect(100, 100, 50, 50);

        _tool.OnPointerDown(At(-40, 500));
        _tool.OnPointerUp(At(-40, 500));

        Assert.Null(_session.Selection);
    }

    [Fact]
    public void ClickInsideSelection_KeepsIt()
    {
        var selection = new PixelRect(100, 100, 50, 50);
        _session.Selection = selection;

        _tool.OnPointerDown(At(120, 130));
        _tool.OnPointerUp(At(120, 130));

        Assert.Equal(selection, _session.Selection);
    }

    [Fact]
    public void SmallMovement_IsClickNotDrag()
    {
        // 押してから DragThreshold 未満しか動かさなければ、クリック（選択範囲の外なら解除）
        _session.Selection = new PixelRect(100, 100, 50, 50);
        double small = SelectTool.DragThreshold - 1;

        _tool.OnPointerDown(At(20, 20));
        _tool.OnPointerMove(At(20 + small, 20 + small));
        _tool.OnPointerUp(At(20 + small, 20 + small));

        Assert.Null(_session.Selection);
    }

    [Fact]
    public void OtherButtons_DoNothing()
    {
        var selection = new PixelRect(100, 100, 50, 50);
        _session.Selection = selection;

        _tool.OnPointerDown(At(10, 10, MouseButton.Right));
        _tool.OnPointerMove(At(200, 200, MouseButton.Right));
        _tool.OnPointerUp(At(200, 200, MouseButton.Right));

        Assert.Equal(selection, _session.Selection);
        Assert.False(_host.Captured);
    }

    [Fact]
    public void MoveWithoutLeftButton_EndsDrag()
    {
        // ドラッグ中に、離したことを受け取れなかった（キャプチャが外れたなど）: 左ボタンを押していない移動で、ドラッグをやめる
        _tool.OnPointerDown(At(10, 10));
        _tool.OnPointerMove(At(60, 30));
        var selection = _session.Selection;

        _tool.OnPointerMove(At(200, 200, null));
        _tool.OnPointerMove(At(300, 250, MouseButton.Left));

        Assert.Equal(selection, _session.Selection);
    }

    [Fact]
    public void MoveWithoutPress_DoesNothing()
    {
        _tool.OnPointerMove(At(100, 100, null));

        Assert.Null(_session.Selection);
    }

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(149.9, 149.9, true)]
    [InlineData(150, 120, false)] // 右端は含まない
    [InlineData(99.9, 120, false)]
    public void Contains_RightAndBottomEdgesExcluded(double x, double y, bool expected)
    {
        Assert.Equal(expected, SelectTool.Contains(new PixelRect(100, 100, 50, 50), new Point(x, y)));
    }
}
