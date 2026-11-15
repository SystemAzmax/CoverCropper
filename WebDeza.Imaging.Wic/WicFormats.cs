using WebDeza.Imaging.Formats;

namespace WebDeza.Imaging.Wic;

/// <summary>
/// 標準の形式（WIC で読み書きするもの）の登録。registry.AddWicFormats() で、設計書 5.4 の 10 形式を登録する。
/// </summary>
public static class WicFormats
{
    // 形式の Id（設定・プリセットに書くので変えない）
    public const string JpegId = "jpeg";
    public const string PngId = "png";
    public const string BmpId = "bmp";
    public const string TiffId = "tiff";
    public const string GifId = "gif";
    public const string JxrId = "jxr";
    public const string IcoId = "ico";
    public const string WebpId = "webp";
    public const string HeifId = "heif";
    public const string AvifId = "avif";

    // 書き出しのオプションのキーと値（設定・プリセットに書くので変えない）
    /// <summary>JPEG の品質（整数 1〜100、既定 85）</summary>
    public const string QualityKey = "quality";
    /// <summary>PNG のインターレース（真偽、既定 false）</summary>
    public const string InterlaceKey = "interlace";
    /// <summary>TIFF の圧縮（"none"・"lzw"・"zip"、既定 "lzw"）</summary>
    public const string CompressionKey = "compression";
    public const string CompressionNone = "none";
    public const string CompressionLzw = "lzw";
    public const string CompressionZip = "zip";

    private const FormatCapabilities Decode = FormatCapabilities.Decode;
    private const FormatCapabilities Encode = FormatCapabilities.Encode;
    private const FormatCapabilities Alpha = FormatCapabilities.Alpha;

    /// <summary>
    /// 標準の形式を登録する。OS の拡張機能が要る形式（WebP・HEIF・AVIF）も常に登録し、
    /// 読めなかったときは UnsupportedImageFormatException に、要る拡張機能の名前（Requirement）が入る
    /// </summary>
    public static void AddWicFormats(this FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(new WicFormat(JpegId, "JPEG", Decode | Encode, WicEncoder.Jpeg(), null, ".jpg", ".jpeg", ".jpe", ".jfif"));
        registry.Register(new WicFormat(PngId, "PNG", Decode | Encode | Alpha, WicEncoder.Png(), null, ".png"));
        registry.Register(new WicFormat(BmpId, "BMP", Decode | Encode, WicEncoder.Bmp(), null, ".bmp", ".dib"));
        registry.Register(new WicFormat(TiffId, "TIFF", Decode | Encode | Alpha, WicEncoder.Tiff(), null, ".tif", ".tiff"));
        // GIF は減色をしないため、書き出しは対象外
        registry.Register(new WicFormat(GifId, "GIF", Decode | Alpha, null, null, ".gif"));
        registry.Register(new WicFormat(JxrId, "JPEG XR", Decode | Alpha, null, null, ".jxr", ".wdp"));
        registry.Register(new WicFormat(IcoId, "ICO", Decode | Alpha, null, null, ".ico"));
        registry.Register(new WicFormat(WebpId, "WebP", Decode | Alpha, null,
            "Microsoft Store の「WebP 画像拡張機能」", ".webp"));
        registry.Register(new WicFormat(HeifId, "HEIF", Decode, null,
            "Microsoft Store の「HEIF 画像拡張機能」（HEVC で圧縮された写真には「HEVC ビデオ拡張機能」も）", ".heic", ".heif"));
        registry.Register(new WicFormat(AvifId, "AVIF", Decode | Alpha, null,
            "Microsoft Store の「AV1 Video Extension」", ".avif"));
    }

    private sealed class WicFormat(string id, string displayName, FormatCapabilities capabilities,
        IImageEncoder? encoder, string? requirement, params string[] extensions) : IImageFormat
    {
        public string Id => id;
        public string DisplayName => displayName;
        public IReadOnlyList<string> Extensions => extensions;
        public FormatCapabilities Capabilities => capabilities;
        public string? Requirement => requirement;
        public IImageDecoder? Decoder { get; } = new WicDecoder(id);
        public IImageEncoder? Encoder => encoder;
    }
}
