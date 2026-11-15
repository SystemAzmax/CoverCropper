namespace WebDeza.ViewModels;

/// <summary>子ウィンドウの並べ方（メニュー［ウィンドウ］。設計書 F-02）</summary>
public enum WindowArrangement
{
    /// <summary>重ねて表示: 左上から少しずつずらす</summary>
    Cascade,

    /// <summary>並べて表示: ワークスペースを格子に分けて、すべてが見えるようにする</summary>
    Tile,
}
