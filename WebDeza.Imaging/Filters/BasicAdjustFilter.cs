using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Filters;

/// <summary>
/// 標準フィルター「基本補正」。明るさ・コントラスト・彩度を変える。アルファは変えない。
/// </summary>
/// <remarks>
/// 明るさ: ガンマ補正。γ = 2^(−明るさ ÷ 100)、v' = 255 × (v ÷ 255)^γ（黒と白は変わらない）。
/// コントラスト: v' = (v − 127.5) × k + 127.5、k = 2^(コントラスト ÷ 100)。
/// 明るさとコントラストは実数のまま続けて計算し、1 回だけ四捨五入して 0〜255 に収めた 256 段の表（LUT）にする。
/// 彩度: 表を適用した後の値から Y = 0.299R + 0.587G + 0.114B を求め、C' = Y + (C − Y) × (1 + 彩度 ÷ 100)。同じように丸める。
/// </remarks>
public sealed class BasicAdjustFilter : IImageFilter
{
    public const string FilterId = "basic";
    public const string BrightnessKey = "brightness";
    public const string ContrastKey = "contrast";
    public const string SaturationKey = "saturation";

    private static readonly ParameterDescriptor[] Descriptors =
    {
        new(BrightnessKey, "明るさ", ParameterKind.Integer, 0, Min: -100, Max: 100),
        new(ContrastKey, "コントラスト", ParameterKind.Integer, 0, Min: -100, Max: 100),
        new(SaturationKey, "彩度", ParameterKind.Integer, 0, Min: -100, Max: 100),
    };

    public string Id => FilterId;

    public string DisplayName => "基本補正";

    public IReadOnlyList<ParameterDescriptor> Parameters => Descriptors;

    public bool IsIdentity(ParameterValues values)
    {
        var (brightness, contrast, saturation) = Read(values);
        return brightness == 0 && contrast == 0 && saturation == 0;
    }

    public ImageBuffer Apply(ImageBuffer source, ParameterValues values, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        var cancellationToken = context.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        var (brightness, contrast, saturation) = Read(values);
        if (brightness == 0 && contrast == 0 && saturation == 0) return source;

        byte[] lut = BuildToneTable(brightness, contrast);
        double factor = 1 + saturation / 100.0;
        bool adjustSaturation = saturation != 0;

        byte[] src = source.Pixels;
        var dst = new byte[src.Length];
        int stride = source.Width * 4;

        Parallel.For(0, source.Height, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            int end = (y + 1) * stride;
            for (int i = y * stride; i < end; i += 4)
            {
                byte b = lut[src[i]];
                byte g = lut[src[i + 1]];
                byte r = lut[src[i + 2]];

                if (adjustSaturation)
                {
                    double luma = 0.299 * r + 0.587 * g + 0.114 * b;
                    b = ToByte(luma + (b - luma) * factor);
                    g = ToByte(luma + (g - luma) * factor);
                    r = ToByte(luma + (r - luma) * factor);
                }

                dst[i] = b;
                dst[i + 1] = g;
                dst[i + 2] = r;
                dst[i + 3] = src[i + 3]; // アルファは変えない
            }
        });

        return ImageBuffer.Create(source.Width, source.Height, dst, source.IsOpaque);
    }

    /// <summary>明るさとコントラストをまとめた 256 段の表。実数のまま続けて計算し、最後に 1 回だけ丸める</summary>
    private static byte[] BuildToneTable(int brightness, int contrast)
    {
        double gamma = Math.Pow(2, -brightness / 100.0);
        double k = Math.Pow(2, contrast / 100.0);

        var table = new byte[256];
        for (int v = 0; v < 256; v++)
        {
            double value = 255 * Math.Pow(v / 255.0, gamma);
            value = (value - 127.5) * k + 127.5;
            table[v] = ToByte(value);
        }
        return table;
    }

    /// <summary>四捨五入（0.5 は 0 から遠い方へ）して 0〜255 に収める</summary>
    private static byte ToByte(double value)
        => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>値を記述に合わせて読む（無いキーは既定値、範囲外は範囲に丸める）</summary>
    private static (int Brightness, int Contrast, int Saturation) Read(ParameterValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var normalized = values.Normalize(Descriptors);
        return (normalized.Get<int>(BrightnessKey), normalized.Get<int>(ContrastKey), normalized.Get<int>(SaturationKey));
    }
}
