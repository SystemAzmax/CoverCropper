using System.Runtime.InteropServices;

namespace WebDeza.Imaging.Geometry;

/// <summary>
/// 向き（回転・反転）を画像に適用する。入力は変えずに、新しい画像を返す。
/// EXIF の番号と画素の対応は、CoverCropper の ImageLoader.ApplyOrientation と同じ。
/// </summary>
public static class Orientation
{
    /// <summary>向きを適用した画像を返す。「そのまま」のときは入力をそのまま返す</summary>
    public static ImageBuffer Apply(ImageBuffer image, ImageOrientation orientation,
        CancellationToken cancellationToken = default)
        => ApplyExif(image, orientation.ToExif(), cancellationToken);

    /// <summary>
    /// EXIF Orientation（1〜8）に従って回転・反転し、正しい向きにした画像を返す。
    /// 1 と、1〜8 以外の値のときは、入力をそのまま返す。
    /// </summary>
    public static ImageBuffer ApplyExif(ImageBuffer image, int exifOrientation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (exifOrientation is < 2 or > 8) return image;

        int width = image.Width, height = image.Height;
        bool swap = exifOrientation >= 5;
        int dstWidth = swap ? height : width;
        int dstHeight = swap ? width : height;
        byte[] src = image.Pixels;
        var dst = new byte[src.Length];

        Parallel.For(0, dstHeight, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            // 出力の 1 行は、入力の画素を start から step ずつ進んで読んだものになる（単位は画素）。
            // 出力 (x, y) が読む入力の画素 (sx, sy) は、番号ごとに次のとおり（添字は sy * width + sx）。
            (int start, int step) = exifOrientation switch
            {
                2 => (y * width + (width - 1), -1),                // 左右反転: (width-1-x, y)
                3 => ((height - 1 - y) * width + (width - 1), -1), // 180°: (width-1-x, height-1-y)
                4 => ((height - 1 - y) * width, 1),                // 上下反転: (x, height-1-y)
                5 => (y, width),                                   // 転置: (y, x)
                6 => ((height - 1) * width + y, -width),           // 時計回り 90°: (y, height-1-x)
                7 => ((height - 1) * width + (width - 1 - y), -width), // 反転置: (width-1-y, height-1-x)
                _ => (width - 1 - y, width),                       // 8 反時計回り 90°: (width-1-y, x)
            };

            // 1 画素（B・G・R・A の 4 バイト）を uint 1 個として写す
            ReadOnlySpan<uint> source = MemoryMarshal.Cast<byte, uint>(src.AsSpan());
            Span<uint> row = MemoryMarshal.Cast<byte, uint>(dst.AsSpan(y * dstWidth * 4, dstWidth * 4));

            if (step == 1)
            {
                source.Slice(start, row.Length).CopyTo(row);
                return;
            }

            for (int x = 0, s = start; x < row.Length; x++, s += step)
                row[x] = source[s];
        });

        // 画素を並べ替えるだけなので、不透明かどうかは変わらない
        return ImageBuffer.Create(dstWidth, dstHeight, dst, image.IsOpaque);
    }
}
