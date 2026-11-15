using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WebDeza.Views.Canvas;

/// <summary>
/// キャンバスのマウスの位置とボタン（設計書 5.6）
/// </summary>
/// <param name="ViewPoint">キャンバスの座標（DIP）</param>
/// <param name="ImagePoint">画像のピクセル座標（実数。画像の外も取りうる）</param>
/// <param name="Button">押した・離したボタン。動かしただけのときは、押しているボタン（無ければ null）</param>
/// <param name="Modifiers">修飾キー</param>
public readonly record struct CanvasPointer(Point ViewPoint, Point ImagePoint, MouseButton? Button, ModifierKeys Modifiers);

/// <summary>
/// キャンバスの道具 1 つの実体（設計書 5.6）。道具の種類は全画像で共通で、実体はキャンバスごとに作る。
/// 中ボタン・Space のパンとホイールのズームはキャンバスが処理し、道具には渡さない
/// </summary>
public interface ICanvasTool
{
    void OnPointerDown(CanvasPointer pointer);

    void OnPointerMove(CanvasPointer pointer);

    void OnPointerUp(CanvasPointer pointer);

    /// <summary>修飾キーの無い（または Shift だけの）キー。処理したら true（道具の切り替えのキーより先に渡す）</summary>
    bool OnKeyDown(Key key, ModifierKeys modifiers);

    /// <summary>ハンドルなどの重ね描き（選択範囲の縁は、どの道具のときもキャンバスが描く）</summary>
    void Render(DrawingContext dc, ViewTransform view);

    /// <summary>この位置でのカーソル（null なら既定）</summary>
    Cursor? GetCursor(CanvasPointer pointer);
}

/// <summary>
/// 道具の種類（設計書 5.6）。ToolRegistry に登録すると、道具のボタンとキーができる。
/// 名前とキーは、画像が無くてもボタンを作れるように、実体ではなくこちらが持つ
/// </summary>
public interface ICanvasToolFactory
{
    /// <summary>道具の ID（例: "hand"）</summary>
    string Id { get; }

    /// <summary>道具の名前（例: "手のひら"）</summary>
    string DisplayName { get; }

    /// <summary>道具を選ぶキー（修飾キーなし。例: H）</summary>
    Key Shortcut { get; }

    /// <summary>キャンバス 1 つ分の道具の実体を作る</summary>
    ICanvasTool Create(IEditorSession session, ICanvasHost host);
}

/// <summary>道具から見たキャンバス（設計書 5.6。EditorCanvas が実装）: 今の表示・再描画の依頼・パン・マウスのキャプチャ</summary>
public interface ICanvasHost
{
    /// <summary>今の表示倍率と位置（描いているとおり）</summary>
    ViewTransform View { get; }

    /// <summary>パンできるか（ズーム中。全体表示のときは画像が領域に収まっているので、パンしても何もしない）</summary>
    bool CanPan { get; }

    /// <summary>描き直しを頼む</summary>
    void InvalidateVisual();

    /// <summary>pointer の位置からパンを始める。pointer のボタンを離すまで、キャンバスがマウスに合わせて表示を動かす</summary>
    void BeginPan(CanvasPointer pointer);

    /// <summary>マウスのキャプチャ（ドラッグ中にキャンバスの外へ出ても、マウスを受け取るため）</summary>
    bool CaptureMouse();

    void ReleaseMouseCapture();
}
