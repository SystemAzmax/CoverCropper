using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WebDeza.Imaging.Wic;

/// <summary>
/// ImageBuffer と WPF の BitmapSource の変換。表示と、CoverCropper（Bgr24 の BitmapSource を使う）のために使う。
/// </summary>
public static class BitmapSourceConverter
{
    /// <summary>ピクセルを写すときに一度に処理する行数（巨大画像で WIC の一時メモリを抑えるため）</summary>
    private const int BandRows = 256;

    /// <summary>表示用の BitmapSource にする（96DPI・Bgra32・Freeze 済み）</summary>
    public static BitmapSource ToBitmapSource(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var bitmap = BitmapSource.Create(
            image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// アルファを捨てた BitmapSource にする（96DPI・Bgr24・Freeze 済み）。
    /// 不透明な画像のエンコードと、CoverCropper が使う。透過のある画像は、先に Flatten で不透明にしておくこと
    /// </summary>
    public static BitmapSource ToBgr24BitmapSource(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int width = image.Width, height = image.Height;
        byte[] src = image.Pixels;
        var dst = new byte[(long)width * height * 3];

        Parallel.For(0, height, y =>
        {
            int s = y * width * 4;
            int d = y * width * 3;
            for (int x = 0; x < width; x++, s += 4, d += 3)
            {
                dst[d] = src[s];
                dst[d + 1] = src[s + 1];
                dst[d + 2] = src[s + 2];
            }
        });

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, dst, width * 3);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// BitmapSource を ImageBuffer にする。どの画素の形式（CMYK・グレースケール・16 bit・パレットなど）も
    /// 8 bit の Bgra32（ストレートアルファ）に変換する。DPI は捨てる
    /// </summary>
    /// <exception cref="ImageTooLargeException">幅 × 高さ × 4 が配列の上限を超える</exception>
    public static ImageBuffer FromBitmapSource(BitmapSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = bgra.PixelWidth, height = bgra.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[ImageBuffer.GetByteLength(width, height)];

        for (int y = 0; y < height; y += BandRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int rows = Math.Min(BandRows, height - y);
            bgra.CopyPixels(new Int32Rect(0, y, width, rows), pixels, stride, y * stride);
        }

        return ImageBuffer.Create(width, height, pixels);
    }
}
