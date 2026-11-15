using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Operations;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Editing;

/// <summary>エンコード済みのファイルの中身</summary>
/// <param name="Bytes">ファイルの中身。書き出すときはこれをそのまま書く（表示したサイズと書き出したファイルのサイズを一致させるため）</param>
/// <param name="FormatId">エンコードした形式の Id</param>
/// <param name="UsedOptions">実際に使ったオプション（記述に合わせたもの。上限を指定したときは探した品質が入る）</param>
/// <param name="FitsSizeLimit">
/// ファイルサイズの上限に収まったか。上限を指定していないとき、または形式がサイズを調整できないときは true。
/// false のときは、品質をいちばん低くしても上限を超えた（M26）
/// </param>
public sealed record EncodedImage(byte[] Bytes, string FormatId, ParameterValues UsedOptions, bool FitsSizeLimit);

/// <summary>
/// 書き出す画像を、書き出し設定の形式でメモリ上にエンコードする（ファイルには書かない）。
/// </summary>
public sealed class ExportEncoder
{
    private readonly FormatRegistry _formats;

    public ExportEncoder(FormatRegistry formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = formats;
    }

    /// <summary>
    /// 書き出す画像（RenderForExport の結果）をエンコードする。
    /// オプションは、書き出し設定のその形式のオプションを、エンコーダーの記述に合わせて使う。
    /// 上限（SizeLimitBytes）があり、エンコーダーが SizeTuning を持つときは、上限以下になるいちばん高い値を探して使う。
    /// SizeTuning を持たない形式では、上限は使わない。
    /// </summary>
    /// <param name="exportImage">書き出す画像</param>
    /// <param name="export">書き出し設定</param>
    /// <param name="cancellationToken">取り消し</param>
    /// <exception cref="UnsupportedImageFormatException">形式が登録されていない、または書き出せない形式</exception>
    /// <exception cref="InvalidOperationException">SizeTuning が、エンコーダーの Integer のオプションを指していない</exception>
    public EncodedImage Encode(ImageBuffer exportImage, ExportSettings export, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exportImage);
        ArgumentNullException.ThrowIfNull(export);
        cancellationToken.ThrowIfCancellationRequested();

        var format = _formats.FindById(export.FormatId);
        if (format is null || !format.Capabilities.HasFlag(FormatCapabilities.Encode) || format.Encoder is null)
            throw new UnsupportedImageFormatException($"書き出せない形式です: {export.FormatId}", export.FormatId, null);

        var encoder = format.Encoder;
        var options = export.GetOptions(format.Id).Normalize(encoder.Options);

        // 透過を保てない形式には、不透明な画像を渡す約束（ふつうは RenderForExport が塗ってある。不透明なら何もしない）
        var image = format.Capabilities.HasFlag(FormatCapabilities.Alpha)
            ? exportImage
            : Flatten.Apply(exportImage, export.Background, cancellationToken);

        byte[] EncodeWith(ParameterValues values)
        {
            using var stream = new MemoryStream();
            encoder.Encode(image, values, stream, cancellationToken);
            return stream.ToArray();
        }

        if (export.SizeLimitBytes is not { } limit || encoder.SizeTuning is not { } tuning)
            return new EncodedImage(EncodeWith(options), format.Id, options, FitsSizeLimit: true);

        // 上限以下になるいちばん高い値を、SizeTuning が指すオプションの範囲（JPEG の品質なら 1〜100）から探す
        var descriptor = encoder.Options.FirstOrDefault(o => string.Equals(o.Key, tuning.ParameterKey, StringComparison.Ordinal));
        if (descriptor is not { Kind: ParameterKind.Integer })
            throw new InvalidOperationException(
                $"形式「{format.Id}」の SizeTuning が指すオプション「{tuning.ParameterKey}」は、整数のオプションではありません。");

        int min = (int)Math.Ceiling(descriptor.Min), max = (int)Math.Floor(descriptor.Max);
        var (value, bytes, fits) = SizeTargetSearch.FindLargest(min, max, limit,
            v => EncodeWith(options.With(tuning.ParameterKey, v)), cancellationToken);

        return new EncodedImage(bytes, format.Id, options.With(tuning.ParameterKey, value), fits);
    }
}
