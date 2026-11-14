namespace WebDeza.Imaging.Operations;

/// <summary>画像から矩形を切り出す。入力は変えずに、新しい画像を返す。</summary>
public static class Crop
{
    /// <summary>
    /// 矩形の範囲を切り出した画像を返す。矩形が画像全体のときは、入力をそのまま返す。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">矩形の幅か高さが 0 以下、または矩形が画像からはみ出す</exception>
    public static ImageBuffer Apply(ImageBuffer image, PixelRect rect, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();

        // X + Width は int の範囲を超えうるので、long で比べる
        if (rect.Width <= 0 || rect.Height <= 0
            || rect.X < 0 || rect.Y < 0
            || (long)rect.X + rect.Width > image.Width
            || (long)rect.Y + rect.Height > image.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), rect,
                $"矩形が {image.Width}×{image.Height} の画像に収まっていません。");
        }

        if (rect.Width == image.Width && rect.Height == image.Height) return image;

        int srcStride = image.Width * 4;
        int dstStride = rect.Width * 4;
        byte[] src = image.Pixels;
        var dst = new byte[ImageBuffer.GetByteLength(rect.Width, rect.Height)];

        for (int y = 0; y < rect.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            src.AsSpan((rect.Y + y) * srcStride + rect.X * 4, dstStride)
                .CopyTo(dst.AsSpan(y * dstStride, dstStride));
        }

        // 不透明な画像の一部は不透明。透過のある画像は、切り出した範囲だけが不透明なこともあるので調べ直す
        return ImageBuffer.Create(rect.Width, rect.Height, dst, image.IsOpaque ? true : null);
    }
}
