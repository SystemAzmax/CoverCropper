using System.Windows.Input;
using System.Windows.Media;

namespace WebDeza.Views.Canvas;

/// <summary>標準の道具「手のひら」（設計書 F-03。キーは H）</summary>
public sealed class HandToolFactory : ICanvasToolFactory
{
    public const string ToolId = "hand";

    public string Id => ToolId;

    public string DisplayName => "手のひら";

    public Key Shortcut => Key.H;

    public ICanvasTool Create(IEditorSession session, ICanvasHost host) => new HandTool(host);
}

/// <summary>
/// 「手のひら」の道具: 左ボタンのドラッグでパンする（画像が領域に収まっているときは何もしない。設計書 4.5）。
/// パンそのものは、中ボタンや Space のパンと同じく、キャンバスが行う
/// </summary>
public sealed class HandTool : ICanvasTool
{
    private readonly ICanvasHost _host;

    public HandTool(ICanvasHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public void OnPointerDown(CanvasPointer pointer)
    {
        if (pointer.Button == MouseButton.Left) _host.BeginPan(pointer);
    }

    public void OnPointerMove(CanvasPointer pointer)
    {
    }

    public void OnPointerUp(CanvasPointer pointer)
    {
    }

    public bool OnKeyDown(Key key, ModifierKeys modifiers) => false;

    public void Render(DrawingContext dc, ViewTransform view)
    {
    }

    /// <summary>パンできるときは移動のカーソル（WPF には「つかむ手」のカーソルが無いため）</summary>
    public Cursor? GetCursor(CanvasPointer pointer) => _host.CanPan ? Cursors.SizeAll : null;
}
