using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverCropper.Services;

public interface IImageLoader
{
    /// <summary>
    /// 元ファイルを読み取り専用で開き、EXIF回転反映・白背景合成済み・96DPIの Bgr24 BitmapSource を返す（Freeze済み）。
    /// </summary>
    BitmapSource Load(string path);
}

public sealed class ImageLoader : IImageLoader
{
    private const string JpegOrientationQuery = "/app1/ifd/{ushort=274}";
    private const string TiffOrientationQuery = "/ifd/{ushort=274}";

    /// <summary>ピクセル変換時に一度に処理する行数（巨大画像で一時バッファを抑えるため）</summary>
    private const int BandRows = 256;

    public BitmapSource Load(string path)
    {
        if (!AppConstants.IsSupportedFile(path))
            throw new NotSupportedException(Messages.M01_UnsupportedFormat);

        byte[] pixels;
        int width, height, orientation;

        // 読み取り専用・OnLoad で読み込み、using を抜けた時点で元ファイルのロックを解放する。
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(
                stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0]; // GIF は1フレーム目のみ

            orientation = ReadOrientation(frame);
            (pixels, width, height) = ToBgr24OnWhite(frame);
        }

        if (orientation is >= 2 and <= 8)
            (pixels, width, height) = ApplyOrientation(pixels, width, height, orientation);

        // DPI 情報は捨て、96DPI で再作成する（以降はピクセル数のみを扱う）
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);
        result.Freeze();
        return result;
    }

    /// <summary>EXIF Orientation（1〜8）を返す。取得できない場合は 1。</summary>
    private static int ReadOrientation(BitmapFrame frame)
    {
        BitmapMetadata? metadata;
        try
        {
            metadata = frame.Metadata as BitmapMetadata;
        }
        catch (NotSupportedException)
        {
            return 1; // BMP 等メタデータ非対応形式
        }
        if (metadata is null) return 1;

        foreach (var query in new[] { JpegOrientationQuery, TiffOrientationQuery })
        {
            try
            {
                if (!metadata.ContainsQuery(query)) continue;
                var value = metadata.GetQuery(query);
                if (value is ushort[] array && array.Length > 0) value = array[0];
                var orientation = Convert.ToInt32(value);
                if (orientation is >= 1 and <= 8) return orientation;
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException
                                          or ArgumentException or FormatException or InvalidCastException
                                          or System.Runtime.InteropServices.COMException)
            {
                // メタデータが壊れている等。回転なしとして扱う。
            }
        }
        return 1;
    }

    /// <summary>
    /// 任意のピクセル形式（CMYK・グレースケール・16bit・インデックス等）を Bgra32 経由で読み出し、
    /// アルファを白背景に合成して Bgr24（stride = width*3）のピクセル配列にする。
    /// </summary>
    private static (byte[] Pixels, int Width, int Height) ToBgr24OnWhite(BitmapSource source)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int srcStride = width * 4;
        int dstStride = width * 3;
        var dst = new byte[(long)dstStride * height];
        var buffer = new byte[(long)srcStride * Math.Min(BandRows, height)];

        for (int y0 = 0; y0 < height; y0 += BandRows)
        {
            int rows = Math.Min(BandRows, height - y0);
            bgra.CopyPixels(new Int32Rect(0, y0, width, rows), buffer, srcStride, 0);

            long d = (long)y0 * dstStride;
            int count = width * rows;
            for (int i = 0, s = 0; i < count; i++, s += 4, d += 3)
            {
                int a = buffer[s + 3];
                if (a == 255)
                {
                    dst[d] = buffer[s];
                    dst[d + 1] = buffer[s + 1];
                    dst[d + 2] = buffer[s + 2];
                }
                else
                {
                    // Bgra32 はストレートアルファ: c*a + 255*(1-a)
                    int inv = 255 * (255 - a);
                    dst[d] = (byte)((buffer[s] * a + inv + 127) / 255);
                    dst[d + 1] = (byte)((buffer[s + 1] * a + inv + 127) / 255);
                    dst[d + 2] = (byte)((buffer[s + 2] * a + inv + 127) / 255);
                }
            }
        }
        return (dst, width, height);
    }

    /// <summary>
    /// EXIF Orientation に従って Bgr24 ピクセル配列を回転・反転し、正しい向きにする。
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) ApplyOrientation(byte[] src, int width, int height, int orientation)
    {
        if (orientation is < 2 or > 8) return (src, width, height);

        bool swap = orientation >= 5;
        int dw = swap ? height : width;
        int dh = swap ? width : height;
        var dst = new byte[(long)dw * dh * 3];

        Parallel.For(0, dh, y =>
        {
            long d = (long)y * dw * 3;
            for (int x = 0; x < dw; x++, d += 3)
            {
                (int sx, int sy) = orientation switch
                {
                    2 => (width - 1 - x, y),              // 左右反転
                    3 => (width - 1 - x, height - 1 - y), // 180度回転
                    4 => (x, height - 1 - y),             // 上下反転
                    5 => (y, x),                          // 転置
                    6 => (y, height - 1 - x),             // 時計回り90度
                    7 => (width - 1 - y, height - 1 - x), // 反転置
                    _ => (width - 1 - y, x),              // 8: 反時計回り90度
                };
                long s = ((long)sy * width + sx) * 3;
                dst[d] = src[s];
                dst[d + 1] = src[s + 1];
                dst[d + 2] = src[s + 2];
            }
        });
        return (dst, dw, dh);
    }
}
