using System.Windows.Media.Imaging;
using WebDeza.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Geometry;
using WebDeza.Imaging.Operations;
using WebDeza.Imaging.Wic;

namespace CoverCropper.Services;

public interface IImageLoader
{
    /// <summary>
    /// 元ファイルを読み取り専用で開き、EXIF回転反映・白背景合成済み・96DPIの Bgr24 BitmapSource を返す（Freeze済み）。
    /// </summary>
    BitmapSource Load(string path);
}

/// <summary>
/// 画像の読み込み。読み込みは共通エンジンの形式レジストリ（WebDeza.Imaging）に任せ、
/// ここでは CoverCropper の決まり（対応する拡張子・白背景・Bgr24）を当てはめる。
/// </summary>
public sealed class ImageLoader : IImageLoader
{
    private static readonly RgbColor White = new(255, 255, 255);

    private readonly FormatRegistry _formats;

    public ImageLoader() : this(DefaultFormats.Registry) { }

    /// <param name="formats">読み込みに使う形式レジストリ</param>
    public ImageLoader(FormatRegistry formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = formats;
    }

    public BitmapSource Load(string path)
    {
        if (!AppConstants.IsSupportedFile(path))
            throw new NotSupportedException(Messages.M01_UnsupportedFormat);

        // 形式レジストリが元ファイルを読み取り専用で開き、読み終えたらすぐ閉じる（ロックを残さない）。
        // 向き（EXIF Orientation）は読み込みの中で直してある
        var decoded = _formats.Decode(path);

        // 透過は白背景に合成し、DPI 情報は捨てて 96DPI にする（以降はピクセル数のみを扱う）
        var flattened = Flatten.Apply(decoded.Image, White);
        return BitmapSourceConverter.ToBgr24BitmapSource(flattened);
    }

    /// <summary>
    /// EXIF Orientation に従って Bgr24 ピクセル配列を回転・反転し、正しい向きにする。
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) ApplyOrientation(byte[] src, int width, int height, int orientation)
    {
        if (orientation is < 2 or > 8) return (src, width, height);

        // エンジンは Bgra32 を扱うので、不透明な Bgra32 にして向きを直し、Bgr24 に戻す
        var bgra = new byte[ImageBuffer.GetByteLength(width, height)];
        for (int s = 0, d = 0; d < bgra.Length; s += 3, d += 4)
        {
            bgra[d] = src[s];
            bgra[d + 1] = src[s + 1];
            bgra[d + 2] = src[s + 2];
            bgra[d + 3] = 255;
        }

        var oriented = Orientation.ApplyExif(ImageBuffer.Create(width, height, bgra, isOpaque: true), orientation);

        var dst = new byte[(long)oriented.Width * oriented.Height * 3];
        byte[] pixels = oriented.Pixels;
        for (int s = 0, d = 0; d < dst.Length; s += 4, d += 3)
        {
            dst[d] = pixels[s];
            dst[d + 1] = pixels[s + 1];
            dst[d + 2] = pixels[s + 2];
        }
        return (dst, oriented.Width, oriented.Height);
    }
}
