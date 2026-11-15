using System.Windows.Media.Imaging;
using WebDeza.Imaging.Editing;

namespace WebDeza.ViewModels;

/// <summary>
/// キャンバスに描く表示用画像（設計書 5.7）。描画用の BitmapSource と、切り抜きの座標系になる編集画像の大きさを組で持つ
/// </summary>
/// <param name="Preview">表示用画像（編集画像を長辺 4096 px 以下に縮小したもの。フィルター前とフィルター後）</param>
/// <param name="Bitmap">Preview.Image を描画用にしたもの（Freeze 済みなので、どのスレッドからも使える）</param>
public sealed record DisplayImage(PreviewImage Preview, BitmapSource Bitmap)
{
    /// <summary>編集画像（向きを適用したフル解像度）の幅。キャンバスはこの大きさとして描く</summary>
    public int EditedWidth => Preview.EditedWidth;

    /// <summary>編集画像の高さ</summary>
    public int EditedHeight => Preview.EditedHeight;

    /// <summary>不透明か（透過が無ければ、市松模様を描かずに済む）</summary>
    public bool IsOpaque => Preview.Image.IsOpaque;
}
