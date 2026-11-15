using System.IO;
using System.Windows.Media.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Geometry;
using WebDeza.Imaging.Operations;

namespace WebDeza.Imaging.Wic;

/// <summary>
/// WIC での読み込み。WIC はファイルの中身を見て形式を判定するので、拡張子と中身が違っていても読める。
/// 1 枚目（ICO はいちばん大きい画像）を読み、向きを直した Bgra32 にする。
/// </summary>
/// <param name="formatId">この読み込みを持つ形式の Id（中身から形式が分からないときと、読めなかったときに使う）</param>
internal sealed class WicDecoder(string formatId) : IImageDecoder
{
    private const string JpegOrientationQuery = "/app1/ifd/{ushort=274}";
    private const string TiffOrientationQuery = "/ifd/{ushort=274}";

    public DecodedImage Decode(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // OnLoad で読み込むので、呼び出し側が stream を閉じた後は元ファイルに触れない
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = SelectFrame(decoder);
            int orientation = ReadOrientation(frame);

            var image = BitmapSourceConverter.FromBitmapSource(frame, cancellationToken);
            image = Orientation.ApplyExif(image, orientation, cancellationToken);

            return new DecodedImage(image, DetectFormatId(decoder) ?? formatId,
                new ImageMetadata(orientation, decoder.Frames.Count));
        }
        catch (NotSupportedException ex) when (ex is not UnsupportedImageFormatException)
        {
            // WIC は、中身が画像でないとき・OS にコーデックが無いときに NotSupportedException を投げる。
            // そのほかの例外（壊れたファイルなど）は、そのまま伝える
            throw new UnsupportedImageFormatException(UnsupportedImageFormatException.DefaultMessage, formatId, null, ex);
        }
    }

    public ImageBuffer DecodeThumbnail(Stream stream, int maxSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSize);

        // 全体を読んでから縮小する（速くする工夫を入れるかどうかは、一覧のサムネイルを作るときに決める）
        var image = Decode(stream, cancellationToken).Image;
        int longest = Math.Max(image.Width, image.Height);
        if (longest <= maxSize) return image;

        int width = Math.Max(1, (int)Math.Round((double)image.Width * maxSize / longest, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round((double)image.Height * maxSize / longest, MidpointRounding.AwayFromZero));
        return Resize.Apply(image, width, height, cancellationToken);
    }

    /// <summary>使うフレーム: ICO はいちばん大きい画像（同じ大きさなら色数の多いもの）、ほかは 1 枚目</summary>
    private static BitmapFrame SelectFrame(BitmapDecoder decoder)
    {
        if (decoder is not IconBitmapDecoder) return decoder.Frames[0];

        var best = decoder.Frames[0];
        foreach (var frame in decoder.Frames)
        {
            long area = (long)frame.PixelWidth * frame.PixelHeight;
            long bestArea = (long)best.PixelWidth * best.PixelHeight;
            if (area > bestArea || (area == bestArea && frame.Format.BitsPerPixel > best.Format.BitsPerPixel))
                best = frame;
        }
        return best;
    }

    /// <summary>中身から分かった形式の Id（標準のコーデックのもの）。分からなければ null</summary>
    private static string? DetectFormatId(BitmapDecoder decoder) => decoder switch
    {
        JpegBitmapDecoder => WicFormats.JpegId,
        PngBitmapDecoder => WicFormats.PngId,
        BmpBitmapDecoder => WicFormats.BmpId,
        TiffBitmapDecoder => WicFormats.TiffId,
        GifBitmapDecoder => WicFormats.GifId,
        WmpBitmapDecoder => WicFormats.JxrId,
        IconBitmapDecoder => WicFormats.IcoId,
        _ => null,
    };

    /// <summary>EXIF Orientation（1〜8）を返す。取得できない場合は 1（CoverCropper の ImageLoader と同じ読み方）</summary>
    private static int ReadOrientation(BitmapFrame frame)
    {
        BitmapMetadata? metadata;
        try
        {
            metadata = frame.Metadata as BitmapMetadata;
        }
        catch (NotSupportedException)
        {
            return 1; // BMP などメタデータ非対応の形式
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
                // メタデータが壊れているなど。回転なしとして扱う
            }
        }
        return 1;
    }
}
