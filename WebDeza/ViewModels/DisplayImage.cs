using System.Windows.Media.Imaging;
using WebDeza.Imaging;
using WebDeza.Imaging.Operations;
using WebDeza.Imaging.Wic;

namespace WebDeza.ViewModels;

/// <summary>
/// キャンバスに描く表示用画像（設計書 5.7）。画像を長辺 4096 px 以下に縮小したものと、描画用の BitmapSource、
/// 選択範囲の座標系になる画像（フル解像度）の大きさを組で持つ
/// </summary>
/// <param name="Image">表示用画像（縮小が要らなければ、画像そのもの）</param>
/// <param name="Bitmap">Image を描画用にしたもの（Freeze 済みなので、どのスレッドからも使える）</param>
/// <param name="ImageWidth">画像（フル解像度）の幅。キャンバスはこの大きさとして描く</param>
/// <param name="ImageHeight">画像（フル解像度）の高さ</param>
public sealed record DisplayImage(ImageBuffer Image, BitmapSource Bitmap, int ImageWidth, int ImageHeight)
{
    /// <summary>表示用画像の長辺の上限（設計書 F-03）。これより大きい画像は縮小して表示する（編集と書き出しには使わない）</summary>
    public const int MaxSide = 4096;

    /// <summary>不透明か（透過が無ければ、市松模様を描かずに済む）</summary>
    public bool IsOpaque => Image.IsOpaque;

    /// <summary>
    /// 画像から表示用画像を作る: 長辺 maxSide 以下に縮小 → 描画用の BitmapSource（設計書 5.8「開く」）。
    /// バックグラウンドで呼んでよい
    /// </summary>
    public static DisplayImage Create(ImageBuffer image, int maxSide = MaxSide, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSide);

        var (width, height) = FitWithin(image.Width, image.Height, maxSide);
        var shown = Resize.Apply(image, width, height, cancellationToken);
        return new DisplayImage(shown, BitmapSourceConverter.ToBitmapSource(shown), image.Width, image.Height);
    }

    /// <summary>長辺が maxSide 以下になる大きさ（縦横比を保ち、四捨五入。各辺は最低 1）。既に収まっていればそのまま</summary>
    internal static (int Width, int Height) FitWithin(int width, int height, int maxSide)
    {
        int longest = Math.Max(width, height);
        if (longest <= maxSide) return (width, height);
        return (Math.Max(1, (int)(((long)width * maxSide * 2 + longest) / (2L * longest))),
                Math.Max(1, (int)(((long)height * maxSide * 2 + longest) / (2L * longest))));
    }
}
