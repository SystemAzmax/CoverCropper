using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Views.Canvas;

namespace WebDeza.Tests.Views;

/// <summary>「選択」の道具: 作る・解除・移動・ハンドル・矢印（設計書 F-04・4.4・4.5。V30。実装計画書 8-5〜8-7）</summary>
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

    // ---------------------------------------------------------------- 移動（選択範囲 (100, 100) 50×50 の内側をドラッグ）

    [Fact]
    public void DragInside_MovesKeepingSize()
    {
        _session.Selection = new PixelRect(100, 100, 50, 50);

        Drag(120, 120, 150, 110);

        Assert.Equal(new PixelRect(130, 90, 50, 50), _session.Selection);
    }

    [Fact]
    public void DragInside_StopsAtImageEdge_AndFollowsWhenBack()
    {
        _session.Selection = new PixelRect(100, 100, 50, 50);

        _tool.OnPointerDown(At(120, 120));
        _tool.OnPointerMove(At(900, 900));
        Assert.Equal(new PixelRect(350, 250, 50, 50), _session.Selection); // 画像 400×300 の右下で止まる

        // 押した所からの移動量で動かすので、戻せばマウスとずれない
        _tool.OnPointerMove(At(130, 130));
        Assert.Equal(new PixelRect(110, 110, 50, 50), _session.Selection);
    }

    [Fact]
    public void DragInside_RoundsToWholePixels()
    {
        // 拡大中（1 px = 2 DIP）: 画像の座標で 0.5 px 未満の移動は 0 px、0.5 px 以上は 1 px
        _host.View = new ViewTransform(2, 0, 0);
        _session.Selection = new PixelRect(100, 100, 50, 50);

        _tool.OnPointerDown(Zoomed(240, 240));
        _tool.OnPointerMove(Zoomed(250.8, 248.8)); // 画像で (+5.4, +4.4)
        Assert.Equal(new PixelRect(105, 104, 50, 50), _session.Selection);
    }

    // ---------------------------------------------------------------- ハンドル（選択範囲 (100, 100) 50×50）

    [Fact]
    public void DragBottomRightHandle_KeepsTopLeft()
    {
        _session.Selection = new PixelRect(100, 100, 50, 50);

        Drag(150, 150, 200, 180);

        Assert.Equal(new PixelRect(100, 100, 100, 80), _session.Selection);
    }

    [Fact]
    public void DragTopLeftHandle_KeepsBottomRight()
    {
        _session.Selection = new PixelRect(100, 100, 50, 50);

        Drag(100, 100, 80, 90);

        Assert.Equal(new PixelRect(80, 90, 70, 60), _session.Selection);
    }

    [Fact]
    public void DragHandle_GrabbedOffCenter_DoesNotJump()
    {
        // ハンドルの角から (2, -3) ずれた所をつかんでも、つかんだ瞬間に枠が飛ばない
        _session.Selection = new PixelRect(100, 100, 50, 50);

        Drag(152, 147, 202, 177);

        Assert.Equal(new PixelRect(100, 100, 100, 80), _session.Selection);
    }

    [Fact]
    public void DragHandle_PastOppositeSide_StopsAtMinimum()
    {
        // 反対側を越えても裏返らず、最小（1 px）で止まる
        _session.Selection = new PixelRect(100, 100, 50, 50);

        Drag(150, 150, 20, 20);

        Assert.Equal(new PixelRect(100, 100, 1, 1), _session.Selection);
    }

    [Fact]
    public void DragHandle_OutsideImage_StopsAtEdge()
    {
        _session.Selection = new PixelRect(100, 100, 50, 50);

        Drag(150, 150, 1000, 1000);

        Assert.Equal(new PixelRect(100, 100, 300, 200), _session.Selection);
    }

    [Fact]
    public void DragEdgeHandles_ChangeOnlyThatSide()
    {
        _session.Selection = new PixelRect(100, 100, 50, 50);
        Drag(150, 125, 180, 200); // 右の辺: 横だけ
        Assert.Equal(new PixelRect(100, 100, 80, 50), _session.Selection);

        _session.Selection = new PixelRect(100, 100, 50, 50);
        Drag(125, 100, 0, 70); // 上の辺: 縦だけ（下の辺を固定）
        Assert.Equal(new PixelRect(100, 70, 50, 80), _session.Selection);
    }

    [Fact]
    public void FixedAspect_CornerHandleKeepsRatio()
    {
        _session.Aspect = new AspectRatio(1, 1);
        _session.Selection = new PixelRect(100, 100, 50, 50);

        Drag(150, 150, 250, 170); // 角: 幅と高さのうち、大きくなる方に合わせる

        Assert.Equal(new PixelRect(100, 100, 150, 150), _session.Selection);
    }

    [Fact]
    public void FixedAspect_NoEdgeHandles()
    {
        // 比率が固定なら辺のハンドルは無い: 右の辺の中ほど（内側）をドラッグすると移動になる
        _session.Aspect = new AspectRatio(1, 1);
        _session.Selection = new PixelRect(100, 100, 50, 50);

        Drag(149, 125, 179, 125);

        Assert.Equal(new PixelRect(130, 100, 50, 50), _session.Selection);
    }

    [Fact]
    public void ClickOnHandle_KeepsSelection()
    {
        // ハンドル（選択範囲の外にはみ出した所も）をドラッグせずにクリックしても、解除しない
        var selection = new PixelRect(100, 100, 50, 50);
        _session.Selection = selection;

        _tool.OnPointerDown(At(153, 153));
        _tool.OnPointerUp(At(153, 153));

        Assert.Equal(selection, _session.Selection);
        Assert.False(_host.Captured);
    }

    [Fact]
    public void Handle_HitTestUsesScreenDistance()
    {
        // 拡大中（1 px = 2 DIP）: 選択範囲は画面で (200, 200) 100×100。右下のハンドルは画面で 6 DIP 以内
        _host.View = new ViewTransform(2, 0, 0);
        _session.Selection = new PixelRect(100, 100, 50, 50);

        _tool.OnPointerDown(Zoomed(305, 305));
        _tool.OnPointerMove(Zoomed(325, 345));

        Assert.Equal(new PixelRect(100, 100, 60, 70), _session.Selection);
    }

    [Fact]
    public void Handles_EdgesOnlyWhenFreeAndLongEnough()
    {
        var big = new Rect(0, 0, 100, 100);
        Assert.Equal(8, SelectTool.Handles(big, withEdges: true).Count);
        Assert.Equal(
            new[] { FrameHandle.TopLeft, FrameHandle.TopRight, FrameHandle.BottomRight, FrameHandle.BottomLeft },
            SelectTool.Handles(big, withEdges: false).Select(h => h.Handle));

        // 短い辺には、辺のハンドルを出さない（幅が短いので上下の辺は無く、左右の辺はある）
        var narrow = new Rect(0, 0, SelectTool.MinSideForEdgeHandle - 1, SelectTool.MinSideForEdgeHandle);
        Assert.Equal(
            new[] { FrameHandle.TopLeft, FrameHandle.TopRight, FrameHandle.BottomRight, FrameHandle.BottomLeft,
                    FrameHandle.Left, FrameHandle.Right },
            SelectTool.Handles(narrow, withEdges: true).Select(h => h.Handle));
    }

    [Fact]
    public void HitHandle_PicksNearest_AndNullOutsideRadius()
    {
        var tiny = new Rect(100, 100, 4, 4); // 四隅のつかめる範囲が重なる
        Assert.Equal(FrameHandle.BottomRight, SelectTool.HitHandle(tiny, true, new Point(103, 103)));
        Assert.Equal(FrameHandle.TopLeft, SelectTool.HitHandle(tiny, true, new Point(101, 99)));

        var rect = new Rect(100, 100, 50, 50);
        Assert.Equal(FrameHandle.Right, SelectTool.HitHandle(rect, true, new Point(155, 120)));
        Assert.Null(SelectTool.HitHandle(rect, false, new Point(155, 120)));
        Assert.Null(SelectTool.HitHandle(rect, true, new Point(100 + SelectTool.HandleHitRadius + 0.1, 100)));
    }

    [Theory]
    [InlineData(1.0, 10, 20, 6, 16)]
    [InlineData(1.5, 10, 20, 6, 16)]
    [InlineData(2.0, 10.4, 20.6, 6.5, 16.5)]
    public void HandleBounds_AlignedToPhysicalPixels(double dpiScale, double centerX, double centerY, double x, double y)
    {
        var bounds = SelectTool.HandleBounds(new Point(centerX, centerY), dpiScale);

        Assert.Equal(new Rect(x, y, SelectTool.HandleSize, SelectTool.HandleSize), bounds);
        Assert.Equal(Math.Round(bounds.Left * dpiScale), bounds.Left * dpiScale, 9);
        Assert.Equal(Math.Round(bounds.Right * dpiScale), bounds.Right * dpiScale, 9);
    }

    [Fact]
    public void Render_DrawsHandlesOnlyWithSelection()
    {
        var view = new ViewTransform(1, 0, 0);
        Assert.Equal(0, RenderCount(view));

        _session.Selection = new PixelRect(100, 100, 50, 50);
        Assert.Equal(8 * 2, RenderCount(view)); // 四隅と四辺。1 つにつき黒い四角と白い四角

        _session.Aspect = new AspectRatio(1, 1);
        Assert.Equal(4 * 2, RenderCount(view)); // 比率が固定なら四隅だけ
    }

    // ---------------------------------------------------------------- 矢印（画像 400×300、選択範囲 (100, 100) 50×50）

    [Theory]
    [InlineData(Key.Left, ModifierKeys.None, 99, 100)]
    [InlineData(Key.Right, ModifierKeys.None, 101, 100)]
    [InlineData(Key.Up, ModifierKeys.None, 100, 99)]
    [InlineData(Key.Down, ModifierKeys.None, 100, 101)]
    [InlineData(Key.Left, ModifierKeys.Shift, 90, 100)]
    [InlineData(Key.Down, ModifierKeys.Shift, 100, 110)]
    public void Arrow_Moves1px_Shift10px(Key key, ModifierKeys modifiers, int x, int y)
    {
        _session.Selection = new PixelRect(100, 100, 50, 50);

        Assert.True(_tool.OnKeyDown(key, modifiers));

        Assert.Equal(new PixelRect(x, y, 50, 50), _session.Selection);
    }

    [Fact]
    public void Arrow_StopsAtImageEdge()
    {
        _session.Selection = new PixelRect(345, 3, 50, 50);

        _tool.OnKeyDown(Key.Right, ModifierKeys.Shift);
        _tool.OnKeyDown(Key.Up, ModifierKeys.Shift);

        Assert.Equal(new PixelRect(350, 0, 50, 50), _session.Selection);
    }

    [Fact]
    public void Arrow_WithoutSelection_UsedButNothingHappens()
    {
        // 選択範囲が無くても矢印は使ったことにする（フォーカスがほかのコントロールへ移らないように）
        Assert.True(_tool.OnKeyDown(Key.Left, ModifierKeys.None));
        Assert.Null(_session.Selection);
    }

    [Fact]
    public void OtherKeys_NotUsed()
    {
        var selection = new PixelRect(100, 100, 50, 50);
        _session.Selection = selection;

        Assert.False(_tool.OnKeyDown(Key.H, ModifierKeys.None));       // 道具の切り替えのキーへ
        Assert.False(_tool.OnKeyDown(Key.Left, ModifierKeys.Control)); // Shift のほかの修飾キーつき
        Assert.Equal(selection, _session.Selection);
    }

    // ---------------------------------------------------------------- カーソル

    [Fact]
    public void Cursor_ByPosition()
    {
        Assert.Same(Cursors.Cross, _tool.GetCursor(At(125, 125, null))); // 選択範囲が無い
        _session.Selection = new PixelRect(100, 100, 50, 50);

        Assert.Same(Cursors.SizeAll, _tool.GetCursor(At(125, 125, null)));  // 内側
        Assert.Same(Cursors.SizeNWSE, _tool.GetCursor(At(100, 100, null))); // 左上
        Assert.Same(Cursors.SizeNWSE, _tool.GetCursor(At(150, 150, null))); // 右下
        Assert.Same(Cursors.SizeNESW, _tool.GetCursor(At(150, 100, null))); // 右上
        Assert.Same(Cursors.SizeNESW, _tool.GetCursor(At(100, 150, null))); // 左下
        Assert.Same(Cursors.SizeNS, _tool.GetCursor(At(125, 100, null)));   // 上の辺
        Assert.Same(Cursors.SizeWE, _tool.GetCursor(At(150, 125, null)));   // 右の辺
        Assert.Same(Cursors.Cross, _tool.GetCursor(At(20, 20, null)));      // 外
    }

    [Fact]
    public void Cursor_DuringDrag_KeepsKindOfDrag()
    {
        _session.Selection = new PixelRect(100, 100, 50, 50);

        _tool.OnPointerDown(At(120, 120));
        _tool.OnPointerMove(At(900, 900));
        Assert.Same(Cursors.SizeAll, _tool.GetCursor(At(900, 900))); // 動かしている間は、外に出ても移動
        _tool.OnPointerUp(At(900, 900));

        _tool.OnPointerDown(At(400, 300)); // 右下のハンドル（選択範囲は (350, 250) 50×50）
        _tool.OnPointerMove(At(10, 10));
        Assert.Same(Cursors.SizeNWSE, _tool.GetCursor(At(10, 10)));
    }

    /// <summary>画面の座標から、拡大中（1 px = 2 DIP）のポインターを作る</summary>
    private static CanvasPointer Zoomed(double viewX, double viewY)
        => new(new Point(viewX, viewY), new Point(viewX / 2, viewY / 2), MouseButton.Left, ModifierKeys.None);

    /// <summary>道具が描いたものの数</summary>
    private int RenderCount(ViewTransform view)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open()) _tool.Render(dc, view);
        return drawing.Children.Count;
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
