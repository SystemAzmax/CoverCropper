using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Filters;

/// <summary>
/// 標準フィルター「ぼかし」（設計書 F-06）。ガウスぼかしを、箱形ぼかし 3 回で近似する。アルファもぼかす。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>標準偏差 σ = 半径 ÷ 2。半径は PixelScale で割ってから使う（縮小した表示用画像でのプレビューと、確定後の見た目をそろえる）。</item>
/// <item>箱形ぼかしは、半径が整数でなくても使えるよう、両端に端数の重みを付けた形（extended box）にする。
///   1 回の分散が σ² ÷ 3 になる半径を求め、横に 3 回、縦に 3 回かける（3 回の分散の合計が σ²）。</item>
/// <item>計算は乗算済みアルファで行う（透明な部分の色が、不透明な部分ににじまない）。</item>
/// <item>画像の端は、端の画素を外へ延ばしたものとして計算する。</item>
/// <item>横の結果は 16 bit（乗算済みの値 × 257）で持ち、縦の計算に渡す（実数で全画面分を持つとメモリが倍になるため）。</item>
/// </list>
/// </remarks>
public sealed class BlurFilter : IImageFilter
{
    public const string FilterId = "blur";
    public const string RadiusKey = "radius";

    /// <summary>半径の最大（px）</summary>
    public const double MaxRadius = 100;

    /// <summary>箱形ぼかしをかける回数（縦・横それぞれ）</summary>
    private const int Passes = 3;

    /// <summary>縦の計算で、まとめて扱う列の数（行ごとに読み書きして、メモリの読み書きをまとめるため）</summary>
    private const int ColumnStrip = 32;

    private const float Scale16 = 257f;

    private static readonly ParameterDescriptor[] Descriptors =
    {
        new(RadiusKey, "半径", ParameterKind.Number, 0.0, Min: 0, Max: MaxRadius, Step: 0.5, Unit: "px"),
    };

    public string Id => FilterId;

    public string DisplayName => "ぼかし";

    public IReadOnlyList<ParameterDescriptor> Parameters => Descriptors;

    public bool IsIdentity(ParameterValues values) => ReadRadius(values) <= 0;

    public ImageBuffer Apply(ImageBuffer source, ParameterValues values, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        var cancellationToken = context.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        if (!(context.PixelScale > 0) || double.IsInfinity(context.PixelScale))
            throw new ArgumentOutOfRangeException(nameof(context), context.PixelScale, "PixelScale は正の数にしてください。");

        double radius = ReadRadius(values) / context.PixelScale;
        if (radius <= 0) return source;

        var (whole, fraction) = BoxRadius(radius / 2);
        int width = source.Width, height = source.Height;
        var options = new ParallelOptions { CancellationToken = cancellationToken };

        // 横: 1 行ずつ、乗算済みの実数にして 3 回かけ、16 bit にして持つ
        var horizontal = new ushort[(long)width * height * 4];
        byte[] src = source.Pixels;
        Parallel.For(0, height, options,
            () => (new float[width * 4], new float[width * 4]),
            (y, _, buffers) =>
            {
                var (a, b) = buffers;
                int rowStart = y * width * 4;
                for (int x = 0, i = rowStart, j = 0; x < width; x++, i += 4, j += 4)
                {
                    float alpha = src[i + 3];
                    float k = alpha / 255f;
                    a[j] = src[i] * k;
                    a[j + 1] = src[i + 1] * k;
                    a[j + 2] = src[i + 2] * k;
                    a[j + 3] = alpha;
                }
                var result = BlurLine(a, b, width, whole, fraction);
                for (int j = 0; j < width * 4; j++)
                    horizontal[rowStart + j] = (ushort)Math.Clamp(MathF.Round(result[j] * Scale16), 0, 65535);
                return buffers;
            },
            _ => { });

        // 縦: 列を ColumnStrip 本ずつまとめて読み、1 列ずつ 3 回かけて、乗算済みを元に戻して書く
        var dst = new byte[src.Length];
        int strips = (width + ColumnStrip - 1) / ColumnStrip;
        Parallel.For(0, strips, options,
            () => (new float[height * 4], new float[height * 4], new float[(long)height * 4 * ColumnStrip]),
            (strip, _, buffers) =>
            {
                var (a, b, columns) = buffers;
                int x0 = strip * ColumnStrip;
                int count = Math.Min(ColumnStrip, width - x0);

                // 行ごとに、隣り合う列をまとめて読む（列 c の値は columns[c * height * 4 + y * 4 ..]）
                for (int y = 0; y < height; y++)
                {
                    int i = (y * width + x0) * 4;
                    for (int c = 0; c < count; c++, i += 4)
                    {
                        int o = (c * height + y) * 4;
                        columns[o] = horizontal[i] / Scale16;
                        columns[o + 1] = horizontal[i + 1] / Scale16;
                        columns[o + 2] = horizontal[i + 2] / Scale16;
                        columns[o + 3] = horizontal[i + 3] / Scale16;
                    }
                }

                for (int c = 0; c < count; c++)
                {
                    Array.Copy(columns, (long)c * height * 4, a, 0, height * 4);
                    var result = BlurLine(a, b, height, whole, fraction);
                    Array.Copy(result, 0, columns, (long)c * height * 4, height * 4);
                }

                for (int y = 0; y < height; y++)
                {
                    int i = (y * width + x0) * 4;
                    for (int c = 0; c < count; c++, i += 4)
                    {
                        int o = (c * height + y) * 4;
                        WriteStraight(columns, o, dst, i);
                    }
                }
                return buffers;
            },
            _ => { });

        if (source.IsOpaque)
        {
            // 不透明な画像は不透明のまま（計算の誤差でアルファが 254 にならないように）
            for (int i = 3; i < dst.Length; i += 4) dst[i] = 255;
            return ImageBuffer.Create(width, height, dst, isOpaque: true);
        }
        return ImageBuffer.Create(width, height, dst);
    }

    /// <summary>
    /// 1 回の分散が variancePerPass（= σ² ÷ 3）になる箱の半径を、整数の部分 l と端数の重み a で返す。
    /// 箱の重みは、−l〜l が 1、±(l + 1) が a（0 ≦ a ＜ 1）。
    /// </summary>
    /// <param name="sigma">ガウスぼかしの標準偏差（px）</param>
    internal static (int Whole, float Fraction) BoxRadius(double sigma)
    {
        double v = sigma * sigma / Passes;
        // 端数なしの箱（幅 2l + 1）の分散は l(l + 1) ÷ 3。それが v 以下になるいちばん大きい l
        int l = (int)Math.Floor((Math.Sqrt(1 + 12 * v) - 1) / 2);
        while ((double)(l + 1) * (l + 2) / 3 <= v) l++;
        while (l > 0 && (double)l * (l + 1) / 3 > v) l--;

        // 分散 = (l(l + 1)(2l + 1) ÷ 3 + 2a(l + 1)²) ÷ (2l + 1 + 2a) を v にする a
        double numerator = v * (2 * l + 1) - l * (l + 1.0) * (2 * l + 1) / 3;
        double denominator = 2 * (l + 1.0) * (l + 1) - 2 * v;
        double a = denominator > 0 ? numerator / denominator : 0;
        return (l, (float)Math.Clamp(a, 0, 1));
    }

    /// <summary>
    /// 4 チャンネルが並んだ n 画素の線に、箱形ぼかしを 3 回かける。結果は a か b のどちらかで、それを返す
    /// </summary>
    private static float[] BlurLine(float[] a, float[] b, int n, int whole, float fraction)
    {
        for (int pass = 0; pass < Passes; pass++)
        {
            BoxPass(a, b, n, whole, fraction);
            (a, b) = (b, a);
        }
        return a;
    }

    /// <summary>
    /// 1 回の箱形ぼかし。出力 x = (Σ_{k=−l..l} p[x + k] + a × (p[x − l − 1] + p[x + l + 1])) ÷ (2l + 1 + 2a)。
    /// 画像の外は端の画素を使う。内側の合計は、1 画素ずらすたびに足し引きして求める
    /// </summary>
    private static void BoxPass(float[] src, float[] dst, int n, int l, float a)
    {
        double weight = 2 * l + 1 + 2 * a;
        double s0 = 0, s1 = 0, s2 = 0, s3 = 0;
        for (int k = -l; k <= l; k++)
        {
            int i = Clamp(k, n) * 4;
            s0 += src[i]; s1 += src[i + 1]; s2 += src[i + 2]; s3 += src[i + 3];
        }

        for (int x = 0; x < n; x++)
        {
            int left = Clamp(x - l - 1, n) * 4;
            int right = Clamp(x + l + 1, n) * 4;
            int o = x * 4;
            dst[o] = (float)((s0 + a * (src[left] + src[right])) / weight);
            dst[o + 1] = (float)((s1 + a * (src[left + 1] + src[right + 1])) / weight);
            dst[o + 2] = (float)((s2 + a * (src[left + 2] + src[right + 2])) / weight);
            dst[o + 3] = (float)((s3 + a * (src[left + 3] + src[right + 3])) / weight);

            // 次の x の内側の合計: x − l の画素を抜き、x + l + 1 の画素を足す
            int drop = Clamp(x - l, n) * 4;
            s0 += src[right] - src[drop];
            s1 += src[right + 1] - src[drop + 1];
            s2 += src[right + 2] - src[drop + 2];
            s3 += src[right + 3] - src[drop + 3];
        }
    }

    private static int Clamp(int index, int n) => index < 0 ? 0 : index >= n ? n - 1 : index;

    /// <summary>乗算済みの値（0〜255 の実数）を、ストレートアルファの Bgra32 にして書く</summary>
    private static void WriteStraight(float[] src, int s, byte[] dst, int d)
    {
        float alpha = src[s + 3];
        byte a = ToByte(alpha);
        if (a == 0)
        {
            dst[d] = dst[d + 1] = dst[d + 2] = dst[d + 3] = 0;
            return;
        }
        float k = 255f / alpha;
        dst[d] = ToByte(src[s] * k);
        dst[d + 1] = ToByte(src[s + 1] * k);
        dst[d + 2] = ToByte(src[s + 2] * k);
        dst[d + 3] = a;
    }

    /// <summary>四捨五入（0.5 は 0 から遠い方へ）して 0〜255 に収める</summary>
    private static byte ToByte(float value)
        => (byte)Math.Clamp(MathF.Round(value, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>値を記述に合わせて読む（無いキーは既定値、範囲外は範囲に丸める）</summary>
    private static double ReadRadius(ParameterValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Normalize(Descriptors).Get<double>(RadiusKey);
    }
}
