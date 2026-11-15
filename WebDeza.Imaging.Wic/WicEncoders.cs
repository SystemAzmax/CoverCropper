using System.IO;
using System.Windows.Media.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Wic;

/// <summary>
/// WIC での書き出し（JPEG / PNG / BMP / TIFF）。メタデータ（サムネイル・EXIF・カラーコンテキスト）は付けない。
/// 不透明な画像は Bgr24、透過のある画像は Bgra32 にして WPF のエンコーダーに渡す。
/// </summary>
internal sealed class WicEncoder : IImageEncoder
{
    private readonly bool _keepsAlpha;
    private readonly Func<ParameterValues, BitmapEncoder> _createEncoder;

    private WicEncoder(IReadOnlyList<ParameterDescriptor> options, SizeTuning? sizeTuning, bool keepsAlpha,
        Func<ParameterValues, BitmapEncoder> createEncoder)
    {
        Options = options;
        SizeTuning = sizeTuning;
        _keepsAlpha = keepsAlpha;
        _createEncoder = createEncoder;
    }

    public IReadOnlyList<ParameterDescriptor> Options { get; }

    public SizeTuning? SizeTuning { get; }

    /// <summary>output はシークできるストリーム（MemoryStream・FileStream）にすること</summary>
    public void Encode(ImageBuffer image, ParameterValues options, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();

        // 透過を保てない形式には不透明な画像が渡される約束。アルファは使わず Bgr24 で書く
        BitmapSource source = _keepsAlpha && !image.IsOpaque
            ? BitmapSourceConverter.ToBitmapSource(image)
            : BitmapSourceConverter.ToBgr24BitmapSource(image);

        var encoder = _createEncoder(options.Normalize(Options));
        // サムネイル・メタデータ・カラーコンテキストはすべて null（位置情報などを書き込まない）
        encoder.Frames.Add(BitmapFrame.Create(source, null, null, null));
        cancellationToken.ThrowIfCancellationRequested();
        encoder.Save(output);
    }

    // ---------------------------------------------------------------- 形式ごとの定義

    public static WicEncoder Jpeg() => new(
        new[]
        {
            new ParameterDescriptor(WicFormats.QualityKey, "品質", ParameterKind.Integer, 85, Min: 1, Max: 100),
        },
        new SizeTuning(WicFormats.QualityKey),
        keepsAlpha: false,
        options => new JpegBitmapEncoder { QualityLevel = options.Get<int>(WicFormats.QualityKey) });

    public static WicEncoder Png() => new(
        new[]
        {
            new ParameterDescriptor(WicFormats.InterlaceKey, "インターレース", ParameterKind.Boolean, false),
        },
        sizeTuning: null,
        keepsAlpha: true,
        options => new PngBitmapEncoder
        {
            Interlace = options.Get<bool>(WicFormats.InterlaceKey) ? PngInterlaceOption.On : PngInterlaceOption.Off,
        });

    public static WicEncoder Bmp() => new(
        Array.Empty<ParameterDescriptor>(),
        sizeTuning: null,
        keepsAlpha: false,
        _ => new BmpBitmapEncoder());

    public static WicEncoder Tiff() => new(
        new[]
        {
            new ParameterDescriptor(WicFormats.CompressionKey, "圧縮", ParameterKind.Choice, WicFormats.CompressionLzw,
                Choices: new ChoiceItem[]
                {
                    new(WicFormats.CompressionNone, "なし"),
                    new(WicFormats.CompressionLzw, "LZW"),
                    new(WicFormats.CompressionZip, "ZIP"),
                }),
        },
        sizeTuning: null,
        keepsAlpha: true,
        options => new TiffBitmapEncoder
        {
            Compression = options.Get<string>(WicFormats.CompressionKey) switch
            {
                WicFormats.CompressionNone => TiffCompressOption.None,
                WicFormats.CompressionZip => TiffCompressOption.Zip,
                _ => TiffCompressOption.Lzw,
            },
        });
}
