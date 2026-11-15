namespace WebDeza.Imaging.Operations;

/// <summary>画像の一部を、別の画像の画素で置き換える。入力は変えずに、新しい画像を返す。</summary>
public static class Paste
{
    /// <summary>
    /// image の (x, y) から patch の大きさの範囲を、patch の画素で置き換えた新しい画像を返す（アルファも含めてそのまま写す。合成はしない）。
    /// patch が画像全体と同じ大きさなら、patch をそのまま返す。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">patch を (x, y) に置くと画像からはみ出す</exception>
    public static ImageBuffer Apply(ImageBuffer image, ImageBuffer patch, int x, int y,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(patch);
        cancellationToken.ThrowIfCancellationRequested();

        // X + Width は int の範囲を超えうるので、long で比べる
        if (x < 0 || y < 0 || (long)x + patch.Width > image.Width || (long)y + patch.Height > image.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(patch), (x, y, patch.Width, patch.Height),
                $"貼る範囲が {image.Width}×{image.Height} の画像に収まっていません。");
        }

        if (patch.Width == image.Width && patch.Height == image.Height) return patch;

        int dstStride = image.Width * 4;
        int srcStride = patch.Width * 4;
        var dst = (byte[])image.Pixels.Clone();
        byte[] src = patch.Pixels;

        for (int row = 0; row < patch.Height; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            src.AsSpan(row * srcStride, srcStride).CopyTo(dst.AsSpan((y + row) * dstStride + x * 4, srcStride));
        }

        // 両方が不透明なら結果も不透明。そうでなければ、置き換えで透過が消えることもあるので調べ直す
        return ImageBuffer.Create(image.Width, image.Height, dst, image.IsOpaque && patch.IsOpaque ? true : null);
    }
}
