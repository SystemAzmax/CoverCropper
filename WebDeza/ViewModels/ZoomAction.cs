namespace WebDeza.ViewModels;

/// <summary>メニュー［表示］とツールバーのズーム（アクティブな子ウィンドウのキャンバスに伝える。設計書 F-03・5.6）</summary>
public enum ZoomAction
{
    /// <summary>表示拡大（中央が中心。1.25 倍）</summary>
    In,

    /// <summary>表示縮小（中央が中心。1 ÷ 1.25 倍）</summary>
    Out,

    /// <summary>全体表示</summary>
    Fit,

    /// <summary>100%（画像の 1 px = 1 物理ピクセル）</summary>
    ActualSize,
}
