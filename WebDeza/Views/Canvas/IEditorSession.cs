using WebDeza.Imaging;
using WebDeza.Imaging.Editing;

namespace WebDeza.Views.Canvas;

/// <summary>
/// キャンバスと道具が、画像 1 枚の選択範囲を読み書きするための窓口（設計書 5.6。DocumentViewModel が実装）
/// </summary>
public interface IEditorSession
{
    /// <summary>画像（今の中身。フル解像度）の幅。選択範囲の座標系の幅</summary>
    int ImageWidth { get; }

    /// <summary>画像の高さ</summary>
    int ImageHeight { get; }

    /// <summary>
    /// 選択範囲（画像のピクセル座標）。null なら無し。入れた値は SelectionRules に合わせて画像の内側に収める。
    /// 選択範囲の操作は履歴に積まない（設計書 F-04）
    /// </summary>
    PixelRect? Selection { get; set; }

    /// <summary>今の画像の大きさと、選択範囲の比率から作った規則</summary>
    FrameRules SelectionRules { get; }

    /// <summary>画像・選択範囲・比率のどれかが変わった（キャンバスは描き直す）</summary>
    event EventHandler? Changed;
}
