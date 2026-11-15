using System.Windows.Media.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.IO;
using WebDeza.Imaging.Parameters;
using WebDeza.Imaging.Wic;

namespace CoverCropper.Services;

public interface IJpegSaver
{
    void Save(BitmapSource image, string path, int quality);
}

/// <summary>
/// JPEG 保存。エンコードは共通の JPEG エンコーダー（WebDeza.Imaging.Wic。メタデータは付けない）、
/// 書き込みは SafeFileWriter に任せる。同フォルダの一時ファイルに書き込んでからリネームするため、
/// 途中で失敗しても壊れた JPEG は残らない。既存ファイルは決して上書きしない。
/// </summary>
public sealed class JpegSaver : IJpegSaver
{
    private readonly IImageEncoder _encoder;

    public JpegSaver() : this(DefaultFormats.Registry) { }

    /// <param name="formats">JPEG のエンコーダーを持つ形式レジストリ</param>
    public JpegSaver(FormatRegistry formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _encoder = formats.FindById(WicFormats.JpegId)?.Encoder
            ?? throw new ArgumentException("JPEG のエンコーダーが登録されていません。", nameof(formats));
    }

    public void Save(BitmapSource image, string path, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);

        var options = ParameterValues.Empty.With(WicFormats.QualityKey, quality);

        // 既にファイルがあるときは、SafeFileWriter が何も書かずに IOException を投げる
        SafeFileWriter.Write(
            path,
            stream => _encoder.Encode(BitmapSourceConverter.FromBitmapSource(image), options, stream, CancellationToken.None),
            protectedPaths: []);
    }
}
