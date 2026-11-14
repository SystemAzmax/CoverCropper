namespace WebDeza.Imaging.Operations;

/// <summary>
/// 透過のある画像を背景色の上に重ねて、不透明な画像にする。入力は変えずに、新しい画像を返す。
/// 透過を保てない形式で書き出すときと、CoverCropper の白背景の合成に使う。
/// </summary>
public static class Flatten
{
    /// <summary>
    /// 背景色で塗って不透明にした画像を返す。既に不透明な画像は、入力をそのまま返す。
    /// </summary>
    /// <remarks>
    /// 式は「(色 × アルファ ＋ 背景色 × (255 − アルファ) ＋ 127) ÷ 255」（整数の割り算）。
    /// 背景色が白のとき、CoverCropper の ImageLoader の白背景の合成と同じ値になる。
    /// </remarks>
    public static ImageBuffer Apply(ImageBuffer image, RgbColor background,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (image.IsOpaque) return image;

        byte[] src = image.Pixels;
        var dst = new byte[src.Length];
        int stride = image.Width * 4;
        int backgroundB = background.B, backgroundG = background.G, backgroundR = background.R;

        Parallel.For(0, image.Height, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            int end = (y + 1) * stride;
            for (int i = y * stride; i < end; i += 4)
            {
                int a = src[i + 3];
                if (a == 255)
                {
                    dst[i] = src[i];
                    dst[i + 1] = src[i + 1];
                    dst[i + 2] = src[i + 2];
                }
                else
                {
                    // Bgra32 はストレートアルファ: 色 × a ＋ 背景色 × (1 − a)
                    int inverse = 255 - a;
                    dst[i] = (byte)((src[i] * a + backgroundB * inverse + 127) / 255);
                    dst[i + 1] = (byte)((src[i + 1] * a + backgroundG * inverse + 127) / 255);
                    dst[i + 2] = (byte)((src[i + 2] * a + backgroundR * inverse + 127) / 255);
                }
                dst[i + 3] = 255;
            }
        });

        return ImageBuffer.Create(image.Width, image.Height, dst, isOpaque: true);
    }
}
