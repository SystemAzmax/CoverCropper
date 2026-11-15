using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;

namespace WebDeza.Views.Canvas;

/// <summary>標準の道具「選択」（設計書 F-03・F-04。キーは M。既定の道具）</summary>
public sealed class SelectToolFactory : ICanvasToolFactory
{
    public const string ToolId = "select";

    public string Id => ToolId;

    public string DisplayName => "選択";

    public Key Shortcut => Key.M;

    public ICanvasTool Create(IEditorSession session, ICanvasHost host) => new SelectTool(session, host);
}

/// <summary>
/// 「選択」の道具（設計書 F-04・4.5）。左ボタンのドラッグで、押した所により次のことをする。
/// ハンドル: 選択範囲の大きさを変える（反対側の角・辺を固定する）。選択範囲の内側: 大きさを保って動かす。
/// そのほか: 新しく作る（始めた点と今の点を対角にする矩形。画像の外へドラッグしたら端で止まる。前の選択範囲は置き換わる）。
/// 選択範囲の外を、ドラッグせずにクリックしたら解除する。選択範囲の操作は履歴に積まない。
/// ハンドルは、比率が自由なら四隅と四辺、決まっていれば四隅（この道具のときだけ描く）。
/// 矢印 / Shift+矢印で、選択範囲を 1 px / 10 px 動かす。
/// </summary>
public sealed class SelectTool : ICanvasTool
{
    /// <summary>押してからこれ以上（DIP）動かしたらドラッグ、それより小さければクリックとみなす</summary>
    public const double DragThreshold = 4;

    /// <summary>矢印 1 回で動かす量（px）</summary>
    public const int KeyStep = 1;

    /// <summary>Shift+矢印 1 回で動かす量（px）</summary>
    public const int ShiftKeyStep = 10;

    /// <summary>ハンドルの四角の大きさ（DIP）</summary>
    public const double HandleSize = 8;

    /// <summary>ハンドルをつかめる範囲（中心からの距離。DIP。縦横それぞれ）</summary>
    public const double HandleHitRadius = 6;

    /// <summary>辺のハンドルは、その辺の画面での長さがこれ以上のときだけ出す（小さい選択範囲で、ハンドルが重ならないように）</summary>
    public const double MinSideForEdgeHandle = 3 * HandleSize;

    private static readonly FrameHandle[] Corners =
        [FrameHandle.TopLeft, FrameHandle.TopRight, FrameHandle.BottomRight, FrameHandle.BottomLeft];

    private readonly IEditorSession _session;
    private readonly ICanvasHost _host;

    /// <summary>ドラッグですること</summary>
    private enum DragKind
    {
        /// <summary>新しく作る</summary>
        Create,

        /// <summary>選択範囲を動かす</summary>
        Move,

        /// <summary>ハンドルで大きさを変える</summary>
        Resize,
    }

    /// <summary>左ボタンを押している間の状態</summary>
    /// <param name="Kind">ドラッグですること</param>
    /// <param name="View">押した位置（画面）</param>
    /// <param name="Image">押した位置（画像。作るときは画像の内側に収めたもの）</param>
    /// <param name="Start">押したときの選択範囲（動かす・大きさを変えるとき）</param>
    /// <param name="Handle">つかんだハンドル（大きさを変えるとき）</param>
    /// <param name="Grab">押した位置と、ハンドルが動かす辺とのずれ（画像。つかんだ瞬間に枠が飛ばないように）</param>
    /// <param name="Dragging">ドラッグになったか</param>
    private readonly record struct Press(
        DragKind Kind, Point View, Point Image, PixelRect Start, FrameHandle Handle, Vector Grab, bool Dragging);

    private Press? _press;

    public SelectTool(IEditorSession session, ICanvasHost host)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(host);
        _session = session;
        _host = host;
    }

    public void OnPointerDown(CanvasPointer pointer)
    {
        if (pointer.Button != MouseButton.Left) return;

        if (_session.Selection is { } selection && HitHandle(selection, pointer.ViewPoint) is { } handle)
        {
            var edge = HandlePoint(new Rect(selection.X, selection.Y, selection.Width, selection.Height), handle);
            _press = new Press(DragKind.Resize, pointer.ViewPoint, pointer.ImagePoint, selection, handle,
                pointer.ImagePoint - edge, false);
        }
        else if (_session.Selection is { } inside && Contains(inside, pointer.ImagePoint))
        {
            _press = new Press(DragKind.Move, pointer.ViewPoint, pointer.ImagePoint, inside, default, default, false);
        }
        else
        {
            _press = new Press(DragKind.Create, pointer.ViewPoint, ClampToImage(pointer.ImagePoint), default, default, default, false);
        }
        _host.CaptureMouse();
    }

    public void OnPointerMove(CanvasPointer pointer)
    {
        if (_press is not { } press) return;
        if (pointer.Button != MouseButton.Left)
        {
            // 離したことを受け取れなかった（キャプチャが外れたなど）: ドラッグをやめる
            _press = null;
            return;
        }
        if (!press.Dragging)
        {
            var moved = pointer.ViewPoint - press.View;
            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold) return;
            press = press with { Dragging = true };
            _press = press;
        }

        var rules = _session.SelectionRules;
        var point = pointer.ImagePoint;
        _session.Selection = press.Kind switch
        {
            DragKind.Move => CropFrameCalculator.Move(press.Start,
                RoundToInt(point.X - press.Image.X), RoundToInt(point.Y - press.Image.Y), rules),
            DragKind.Resize => CropFrameCalculator.ResizeFromHandle(press.Start, press.Handle,
                point.X - press.Grab.X, point.Y - press.Grab.Y, rules),
            _ => CropFrameCalculator.FromDrag(press.Image.X, press.Image.Y, point.X, point.Y, rules),
        };
    }

    public void OnPointerUp(CanvasPointer pointer)
    {
        if (pointer.Button != MouseButton.Left || _press is not { } press) return;
        _press = null;
        _host.ReleaseMouseCapture();

        // ドラッグせずに、選択範囲の外（ハンドルでもない所）をクリックした: 解除する
        if (press is { Kind: DragKind.Create, Dragging: false } && _session.Selection is not null)
            _session.Selection = null;
    }

    /// <summary>
    /// 矢印で選択範囲を 1 px、Shift+矢印で 10 px 動かす（設計書 4.4。画像の端で止まる）。
    /// 矢印は、選択範囲が無いときも使ったことにする（フォーカスが矢印でほかのコントロールへ移らないように）
    /// </summary>
    public bool OnKeyDown(Key key, ModifierKeys modifiers)
    {
        var (dx, dy) = key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };
        if ((dx, dy) == (0, 0) || modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return false;

        if (_session.Selection is { } selection)
        {
            int step = modifiers == ModifierKeys.Shift ? ShiftKeyStep : KeyStep;
            _session.Selection = CropFrameCalculator.Move(selection, dx * step, dy * step, _session.SelectionRules);
        }
        return true;
    }

    /// <summary>ハンドルを描く（白い四角に黒い縁。辺は物理ピクセルにそろえる）</summary>
    public void Render(DrawingContext dc, ViewTransform view)
    {
        if (_session.Selection is not { } selection) return;
        double dpiScale = _host.DpiScale;
        var bounds = EditorCanvas.ImageRectToView(selection, view, dpiScale);
        double border = 1 / dpiScale;
        foreach (var (_, center) in Handles(bounds, WithEdgeHandles))
        {
            var outer = HandleBounds(center, dpiScale);
            dc.DrawRectangle(Brushes.Black, null, outer);
            if (outer.Width > 2 * border && outer.Height > 2 * border)
                dc.DrawRectangle(Brushes.White, null, new Rect(
                    outer.X + border, outer.Y + border, outer.Width - 2 * border, outer.Height - 2 * border));
        }
    }

    /// <summary>
    /// カーソル: ドラッグ中はしていることのカーソル。そのほかは、ハンドルなら向きの矢印、選択範囲の内側なら移動、外なら十字
    /// </summary>
    public Cursor? GetCursor(CanvasPointer pointer)
    {
        if (_press is { } press)
        {
            return press.Kind switch
            {
                DragKind.Move => Cursors.SizeAll,
                DragKind.Resize => CursorFor(press.Handle),
                _ => Cursors.Cross,
            };
        }
        if (_session.Selection is not { } selection) return Cursors.Cross;
        if (HitHandle(selection, pointer.ViewPoint) is { } handle) return CursorFor(handle);
        return Contains(selection, pointer.ImagePoint) ? Cursors.SizeAll : Cursors.Cross;
    }

    // ---------------------------------------------------------------- 計算（画面を使わずにテストできる）

    /// <summary>画像のピクセル座標の点が、矩形の内側か（右端・下端は含まない）</summary>
    public static bool Contains(PixelRect rect, Point imagePoint)
        => imagePoint.X >= rect.X && imagePoint.X < rect.X + rect.Width
           && imagePoint.Y >= rect.Y && imagePoint.Y < rect.Y + rect.Height;

    /// <summary>
    /// ハンドルとその中心（画面の座標）。四隅は必ず、辺は withEdges のときに、その辺の長さが
    /// MinSideForEdgeHandle 以上なら出す
    /// </summary>
    /// <param name="selection">選択範囲（画面の矩形）</param>
    /// <param name="withEdges">辺のハンドルを出すか（比率が自由のとき）</param>
    public static IReadOnlyList<(FrameHandle Handle, Point Center)> Handles(Rect selection, bool withEdges)
    {
        var handles = new List<(FrameHandle, Point)>(8);
        foreach (var corner in Corners) handles.Add((corner, HandlePoint(selection, corner)));
        if (withEdges)
        {
            if (selection.Width >= MinSideForEdgeHandle)
            {
                handles.Add((FrameHandle.Top, HandlePoint(selection, FrameHandle.Top)));
                handles.Add((FrameHandle.Bottom, HandlePoint(selection, FrameHandle.Bottom)));
            }
            if (selection.Height >= MinSideForEdgeHandle)
            {
                handles.Add((FrameHandle.Left, HandlePoint(selection, FrameHandle.Left)));
                handles.Add((FrameHandle.Right, HandlePoint(selection, FrameHandle.Right)));
            }
        }
        return handles;
    }

    /// <summary>
    /// viewPoint でつかめるハンドル（中心から縦横とも HandleHitRadius 以内。いくつも当たるときは、いちばん近いもの）。無ければ null
    /// </summary>
    /// <param name="selection">選択範囲（画面の矩形）</param>
    /// <param name="withEdges">辺のハンドルを出すか（比率が自由のとき）</param>
    /// <param name="viewPoint">マウスの位置（画面の座標）</param>
    public static FrameHandle? HitHandle(Rect selection, bool withEdges, Point viewPoint)
    {
        FrameHandle? hit = null;
        double nearest = double.PositiveInfinity;
        foreach (var (handle, center) in Handles(selection, withEdges))
        {
            var d = viewPoint - center;
            if (Math.Abs(d.X) > HandleHitRadius || Math.Abs(d.Y) > HandleHitRadius) continue;
            double distance = d.LengthSquared;
            if (distance < nearest)
            {
                nearest = distance;
                hit = handle;
            }
        }
        return hit;
    }

    /// <summary>ハンドルの四角（中心から HandleSize 角。辺は物理ピクセルの境目にそろえる）</summary>
    public static Rect HandleBounds(Point center, double dpiScale)
    {
        double Snap(double value) => Math.Round(value * dpiScale) / dpiScale;
        double half = HandleSize / 2;
        double left = Snap(center.X - half), top = Snap(center.Y - half);
        return new Rect(left, top, Snap(center.X + half) - left, Snap(center.Y + half) - top);
    }

    /// <summary>ハンドルのカーソル（斜め・上下・左右の矢印）</summary>
    public static Cursor CursorFor(FrameHandle handle) => handle switch
    {
        FrameHandle.TopLeft or FrameHandle.BottomRight => Cursors.SizeNWSE,
        FrameHandle.TopRight or FrameHandle.BottomLeft => Cursors.SizeNESW,
        FrameHandle.Top or FrameHandle.Bottom => Cursors.SizeNS,
        _ => Cursors.SizeWE,
    };

    // ---------------------------------------------------------------- 内部

    /// <summary>比率が自由なら、辺のハンドルも出す（設計書 F-04）</summary>
    private bool WithEdgeHandles => _session.SelectionRules.Aspect is null;

    private FrameHandle? HitHandle(PixelRect selection, Point viewPoint)
        => HitHandle(EditorCanvas.ImageRectToView(selection, _host.View, _host.DpiScale), WithEdgeHandles, viewPoint);

    /// <summary>矩形の、ハンドルの位置（角、または辺の中点）</summary>
    private static Point HandlePoint(Rect rect, FrameHandle handle)
    {
        double left = rect.Left, right = rect.Right, top = rect.Top, bottom = rect.Bottom;
        double centerX = (left + right) / 2, centerY = (top + bottom) / 2;
        return handle switch
        {
            FrameHandle.TopLeft => new(left, top),
            FrameHandle.Top => new(centerX, top),
            FrameHandle.TopRight => new(right, top),
            FrameHandle.Right => new(right, centerY),
            FrameHandle.BottomRight => new(right, bottom),
            FrameHandle.Bottom => new(centerX, bottom),
            FrameHandle.BottomLeft => new(left, bottom),
            _ => new(left, centerY),
        };
    }

    /// <summary>ドラッグの始点は、画像の内側（画素の境目の座標で 0〜幅・0〜高さ）に収める</summary>
    private Point ClampToImage(Point point)
        => new(Math.Clamp(point.X, 0, _session.ImageWidth), Math.Clamp(point.Y, 0, _session.ImageHeight));

    /// <summary>四捨五入（0.5 は 0 から遠い方へ）して int に収める</summary>
    private static int RoundToInt(double value)
        => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
}
