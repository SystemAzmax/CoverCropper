namespace WebDeza.Imaging.Operations;

/// <summary>
/// 画像を指定の大きさにリサイズする。入力は変えずに、新しい画像を返す。
/// 補間は Lanczos3 の分離型畳み込み（横方向 → 縦方向）で、縮小のときはカーネルの幅を縮小率に応じて広げる。
/// </summary>
/// <remarks>
/// 不透明な画像の計算（重みの作り方・足す順番・丸め）は、CoverCropper の ImageResizer.ResizePixels と同じにしてある。
/// CoverCropper の出力を変えないためなので、ここを変えるときは基準ファイルとの一致（テスト E07）を確かめること。
/// </remarks>
public static class Resize
{
    private const int LanczosA = 3;

    /// <summary>
    /// 幅 × 高さにリサイズした画像を返す。大きさが同じときは、入力をそのまま返す。
    /// 透過のある画像は、透明な部分の色がにじまないよう、色にアルファを掛けた値（乗算済みアルファ）で計算する。
    /// </summary>
    public static ImageBuffer Apply(ImageBuffer image, int width, int height,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        int length = ImageBuffer.GetByteLength(width, height);
        cancellationToken.ThrowIfCancellationRequested();

        if (width == image.Width && height == image.Height) return image;

        // 途中の結果（入力の行数 × 出力の幅）も 1 つの配列に持つので、上限を超えないことを確かめておく
        int channels = image.IsOpaque ? 3 : 4;
        if ((long)image.Height * width * channels > Array.MaxLength)
            throw new ImageTooLargeException(image.Width, image.Height);

        var horizontal = Kernel.Build(image.Width, width);
        var vertical = Kernel.Build(image.Height, height);
        var options = new ParallelOptions { CancellationToken = cancellationToken };
        var dst = new byte[length];

        if (image.IsOpaque)
        {
            ResizeOpaque(image, width, height, horizontal, vertical, dst, options);
            return ImageBuffer.Create(width, height, dst, isOpaque: true);
        }

        ResizeWithAlpha(image, width, height, horizontal, vertical, dst, options);
        return ImageBuffer.Create(width, height, dst);
    }

    /// <summary>
    /// 不透明な画像: B・G・R の 3 チャンネルを CoverCropper と同じ順番で計算し、アルファは 255 にする。
    /// </summary>
    private static void ResizeOpaque(ImageBuffer image, int dw, int dh, Kernel hk, Kernel vk, byte[] dst,
        ParallelOptions options)
    {
        byte[] src = image.Pixels;
        int sw = image.Width, sh = image.Height;
        int srcStride = sw * 4;
        int rowLen = dw * 3;

        // 1. 横方向: sh 行 × dw 列（float）
        var tmp = new float[sh * rowLen];
        Parallel.For(0, sh, options, y =>
        {
            int srcRow = y * srcStride;
            int tmpRow = y * rowLen;
            for (int x = 0; x < dw; x++)
            {
                int start = hk.Start[x], count = hk.Count[x], wBase = x * hk.Taps;
                float b = 0, g = 0, r = 0;
                int s = srcRow + start * 4;
                for (int k = 0; k < count; k++, s += 4)
                {
                    float w = hk.Weights[wBase + k];
                    b += src[s] * w;
                    g += src[s + 1] * w;
                    r += src[s + 2] * w;
                }
                int t = tmpRow + x * 3;
                tmp[t] = b;
                tmp[t + 1] = g;
                tmp[t + 2] = r;
            }
        });

        // 2. 縦方向: 出力行ごとに入力行を重み付き加算
        Parallel.For(0, dh, options,
            () => new float[rowLen],
            (y, _, acc) =>
            {
                Array.Clear(acc);
                int start = vk.Start[y], count = vk.Count[y], wBase = y * vk.Taps;
                for (int k = 0; k < count; k++)
                {
                    float w = vk.Weights[wBase + k];
                    int row = (start + k) * rowLen;
                    for (int i = 0; i < rowLen; i++)
                        acc[i] += tmp[row + i] * w;
                }

                int d = y * dw * 4;
                for (int i = 0; i < rowLen; i += 3, d += 4)
                {
                    dst[d] = ToByte(acc[i]);
                    dst[d + 1] = ToByte(acc[i + 1]);
                    dst[d + 2] = ToByte(acc[i + 2]);
                    dst[d + 3] = 255;
                }
                return acc;
            },
            _ => { });
    }

    /// <summary>
    /// 透過のある画像: 色にアルファを掛けた値（乗算済みアルファ）とアルファを補間し、最後にアルファで割って色に戻す。
    /// 透明な画素の色は重み 0 になるので、不透明な部分の色に混ざらない。
    /// </summary>
    private static void ResizeWithAlpha(ImageBuffer image, int dw, int dh, Kernel hk, Kernel vk, byte[] dst,
        ParallelOptions options)
    {
        byte[] src = image.Pixels;
        int sw = image.Width, sh = image.Height;
        int srcStride = sw * 4;
        int rowLen = dw * 4;

        // 1. 横方向。色は「色 × アルファ」（0〜65025）のまま持ち、最後にアルファ（0〜255）で割る
        var tmp = new float[sh * rowLen];
        Parallel.For(0, sh, options, y =>
        {
            int srcRow = y * srcStride;
            int tmpRow = y * rowLen;
            for (int x = 0; x < dw; x++)
            {
                int start = hk.Start[x], count = hk.Count[x], wBase = x * hk.Taps;
                float b = 0, g = 0, r = 0, a = 0;
                int s = srcRow + start * 4;
                for (int k = 0; k < count; k++, s += 4)
                {
                    float wa = src[s + 3] * hk.Weights[wBase + k];
                    b += src[s] * wa;
                    g += src[s + 1] * wa;
                    r += src[s + 2] * wa;
                    a += wa;
                }
                int t = tmpRow + x * 4;
                tmp[t] = b;
                tmp[t + 1] = g;
                tmp[t + 2] = r;
                tmp[t + 3] = a;
            }
        });

        // 2. 縦方向
        Parallel.For(0, dh, options,
            () => new float[rowLen],
            (y, _, acc) =>
            {
                Array.Clear(acc);
                int start = vk.Start[y], count = vk.Count[y], wBase = y * vk.Taps;
                for (int k = 0; k < count; k++)
                {
                    float w = vk.Weights[wBase + k];
                    int row = (start + k) * rowLen;
                    for (int i = 0; i < rowLen; i++)
                        acc[i] += tmp[row + i] * w;
                }

                int d = y * rowLen;
                for (int i = 0; i < rowLen; i += 4)
                {
                    float a = acc[i + 3];
                    byte alpha = ToByte(a);
                    if (alpha == 0)
                    {
                        // 透明な画素の色は意味を持たないので、透明な黒にそろえる
                        dst[d + i] = dst[d + i + 1] = dst[d + i + 2] = dst[d + i + 3] = 0;
                        continue;
                    }

                    // alpha が 1 以上なら a は 0.5 以上なので、0 で割ることはない
                    dst[d + i] = ToByte(acc[i] / a);
                    dst[d + i + 1] = ToByte(acc[i + 1] / a);
                    dst[d + i + 2] = ToByte(acc[i + 2] / a);
                    dst[d + i + 3] = alpha;
                }
                return acc;
            },
            _ => { });
    }

    /// <summary>四捨五入して 0〜255 に収める（CoverCropper の ImageResizer と同じ丸め方）</summary>
    private static byte ToByte(float value)
    {
        int v = (int)(value + 0.5f);
        return (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
    }

    private static double Lanczos(double x)
    {
        if (x == 0) return 1.0;
        if (x <= -LanczosA || x >= LanczosA) return 0.0;
        double px = Math.PI * x;
        return LanczosA * Math.Sin(px) * Math.Sin(px / LanczosA) / (px * px);
    }

    /// <summary>1 次元リサンプリング用の重みテーブル（CoverCropper の ImageResizer.Kernel と同じ作り方）</summary>
    private sealed class Kernel
    {
        public required int[] Start { get; init; }
        public required int[] Count { get; init; }
        public required float[] Weights { get; init; }
        public required int Taps { get; init; }

        public static Kernel Build(int srcSize, int dstSize)
        {
            double scale = (double)dstSize / srcSize;
            double filterScale = Math.Max(1.0, 1.0 / scale); // 縮小時はカーネルを広げる
            double support = LanczosA * filterScale;
            int taps = (int)Math.Ceiling(support * 2) + 2;

            var start = new int[dstSize];
            var count = new int[dstSize];
            var weights = new float[(long)dstSize * taps];

            for (int i = 0; i < dstSize; i++)
            {
                double center = (i + 0.5) / scale;
                int left = Math.Max(0, (int)Math.Floor(center - support));
                int right = Math.Min(srcSize, (int)Math.Ceiling(center + support));

                int n = 0;
                double sum = 0;
                for (int j = left; j < right && n < taps; j++, n++)
                {
                    double w = Lanczos((j + 0.5 - center) / filterScale);
                    weights[i * taps + n] = (float)w;
                    sum += w;
                }

                if (Math.Abs(sum) < 1e-12)
                {
                    // 念のため: 最近傍にフォールバック
                    left = Math.Clamp((int)center, 0, srcSize - 1);
                    n = 1;
                    weights[i * taps] = 1f;
                }
                else
                {
                    for (int k = 0; k < n; k++)
                        weights[i * taps + k] = (float)(weights[i * taps + k] / sum);
                }

                start[i] = left;
                count[i] = n;
            }

            return new Kernel { Start = start, Count = count, Weights = weights, Taps = taps };
        }
    }
}
