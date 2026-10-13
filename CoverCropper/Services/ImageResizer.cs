using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverCropper.Services;

public interface IImageResizer
{
    /// <summary>F-02 の計算式に従ったリサイズ後サイズを返す（純粋関数）</summary>
    (int Width, int Height) CalculateSize(int srcWidth, int srcHeight);

    /// <summary>高品質補間でリサイズし、96DPI・Bgr24 の Freeze 済み画像を返す</summary>
    BitmapSource Resize(BitmapSource source, int width, int height);
}

/// <summary>
/// 比率維持リサイズ。補間は Lanczos3 の分離型畳み込みで、縮小時はカーネル幅を縮小率に応じて広げるため
/// 大きな縮小率でもジャギーやモアレが出にくい（WPF の HighQuality(Fant) 以上の品質）。
/// スレッドに依存しない純粋なピクセル処理なので Task.Run 上で安全に実行できる。
/// </summary>
public sealed class ImageResizer : IImageResizer
{
    private const int LanczosA = 3;

    public (int Width, int Height) CalculateSize(int srcWidth, int srcHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(srcWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(srcHeight);

        int newW = AppConstants.TargetWidth;
        int newH = RoundToInt((double)srcHeight * AppConstants.TargetWidth / srcWidth);

        if (newH < AppConstants.TargetHeight)
        {
            newH = AppConstants.TargetHeight;
            newW = Math.Max(AppConstants.TargetWidth,
                RoundToInt((double)srcWidth * AppConstants.TargetHeight / srcHeight));
        }
        return (newW, newH);
    }

    public BitmapSource Resize(BitmapSource source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        BitmapSource bgr = source.Format == PixelFormats.Bgr24
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);

        int sw = bgr.PixelWidth, sh = bgr.PixelHeight;
        int srcStride = sw * 3;
        var src = new byte[(long)srcStride * sh];
        bgr.CopyPixels(src, srcStride, 0);

        var dst = ResizePixels(src, sw, sh, width, height);

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, dst, width * 3);
        result.Freeze();
        return result;
    }

    /// <summary>Bgr24（stride = 幅*3）のピクセル配列をリサイズする</summary>
    internal static byte[] ResizePixels(byte[] src, int sw, int sh, int dw, int dh)
    {
        var hk = Kernel.Build(sw, dw);
        var vk = Kernel.Build(sh, dh);
        int srcStride = sw * 3;
        int rowLen = dw * 3;

        // 1. 横方向: sh 行 × dw 列（float）
        var tmp = new float[(long)sh * rowLen];
        Parallel.For(0, sh, y =>
        {
            long srcRow = (long)y * srcStride;
            long tmpRow = (long)y * rowLen;
            for (int x = 0; x < dw; x++)
            {
                int start = hk.Start[x], count = hk.Count[x], wBase = x * hk.Taps;
                float b = 0, g = 0, r = 0;
                long s = srcRow + start * 3L;
                for (int k = 0; k < count; k++, s += 3)
                {
                    float w = hk.Weights[wBase + k];
                    b += src[s] * w;
                    g += src[s + 1] * w;
                    r += src[s + 2] * w;
                }
                long t = tmpRow + x * 3L;
                tmp[t] = b;
                tmp[t + 1] = g;
                tmp[t + 2] = r;
            }
        });

        // 2. 縦方向: 出力行ごとに入力行を重み付き加算
        var dst = new byte[(long)dh * rowLen];
        Parallel.For(0, dh,
            () => new float[rowLen],
            (y, _, acc) =>
            {
                Array.Clear(acc);
                int start = vk.Start[y], count = vk.Count[y], wBase = y * vk.Taps;
                for (int k = 0; k < count; k++)
                {
                    float w = vk.Weights[wBase + k];
                    long row = (long)(start + k) * rowLen;
                    for (int i = 0; i < rowLen; i++)
                        acc[i] += tmp[row + i] * w;
                }
                long d = (long)y * rowLen;
                for (int i = 0; i < rowLen; i++)
                {
                    int v = (int)(acc[i] + 0.5f);
                    dst[d + i] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                }
                return acc;
            },
            _ => { });

        return dst;
    }

    private static int RoundToInt(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static double Lanczos(double x)
    {
        if (x == 0) return 1.0;
        if (x <= -LanczosA || x >= LanczosA) return 0.0;
        double px = Math.PI * x;
        return LanczosA * Math.Sin(px) * Math.Sin(px / LanczosA) / (px * px);
    }

    /// <summary>1次元リサンプリング用の重みテーブル</summary>
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
