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
/// 「選択」の道具（設計書 F-04）: キャンバスをドラッグして選択範囲を作る（始めた点と今の点を対角にする矩形。
/// 画像の外へドラッグしたら端で止まる。前の選択範囲は置き換わる）。選択範囲の外を、ドラッグせずにクリックしたら解除する。
/// 選択範囲の操作は履歴に積まない。
/// </summary>
/// <remarks>選択範囲の内側のドラッグでの移動・ハンドルは 8-6、矢印キーでの移動は 8-7 で足す。</remarks>
public sealed class SelectTool : ICanvasTool
{
    /// <summary>押してからこれ以上（DIP）動かしたらドラッグ、それより小さければクリックとみなす</summary>
    public const double DragThreshold = 4;

    private readonly IEditorSession _session;
    private readonly ICanvasHost _host;

    /// <summary>左ボタンを押している間: 押した位置（画面と画像）と、ドラッグになったか</summary>
    private (Point View, Point Image, bool Dragging)? _press;

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
        _press = (pointer.ViewPoint, ClampToImage(pointer.ImagePoint), false);
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
            _press = press with { Dragging = true };
        }

        _session.Selection = CropFrameCalculator.FromDrag(
            press.Image.X, press.Image.Y, pointer.ImagePoint.X, pointer.ImagePoint.Y, _session.SelectionRules);
    }

    public void OnPointerUp(CanvasPointer pointer)
    {
        if (pointer.Button != MouseButton.Left || _press is not { } press) return;
        _press = null;
        _host.ReleaseMouseCapture();

        // ドラッグせずにクリックした: 選択範囲の外なら解除する
        if (!press.Dragging && _session.Selection is { } selection && !Contains(selection, pointer.ImagePoint))
            _session.Selection = null;
    }

    public bool OnKeyDown(Key key, ModifierKeys modifiers) => false;

    public void Render(DrawingContext dc, ViewTransform view)
    {
    }

    public Cursor? GetCursor(CanvasPointer pointer) => Cursors.Cross;

    /// <summary>画像のピクセル座標の点が、矩形の内側か（右端・下端は含まない）</summary>
    public static bool Contains(PixelRect rect, Point imagePoint)
        => imagePoint.X >= rect.X && imagePoint.X < rect.X + rect.Width
           && imagePoint.Y >= rect.Y && imagePoint.Y < rect.Y + rect.Height;

    /// <summary>ドラッグの始点は、画像の内側（画素の境目の座標で 0〜幅・0〜高さ）に収める</summary>
    private Point ClampToImage(Point point)
        => new(Math.Clamp(point.X, 0, _session.ImageWidth), Math.Clamp(point.Y, 0, _session.ImageHeight));
}
