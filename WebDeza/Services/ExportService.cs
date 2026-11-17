using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.IO;

namespace WebDeza.Services;

/// <summary>保存したファイル</summary>
/// <param name="Path">保存したファイルのフルパス</param>
/// <param name="Encoded">書いた中身（バイト数・使ったオプション・上限に収まったか）</param>
public sealed record ExportResult(string Path, EncodedImage Encoded)
{
    /// <summary>保存したファイルのバイト数</summary>
    public long FileSize => Encoded.Bytes.LongLength;
}

/// <summary>
/// 保存するファイルのパスを決める（出力先のフォルダ・元画像のパス・日時・拡張子から。ふつうは OutputFileNamer.CreatePath）
/// </summary>
public delegate string OutputPathFactory(string outputFolder, string sourcePath, DateTime now, string extension);

/// <summary>
/// 1 枚の保存（設計書 5.6・5.8・F-10）: 今の画像 → ExportRenderer.Render（透過を保てない形式なら背景色で塗る）
/// → ExportEncoder.Encode（メモリ上に作る）→ SafeFileWriter.Write（一時ファイル → リネーム。元画像のパスには書かない）。
/// ファイル名は「元名_yyyyMMdd_HHmmss.{形式の先頭の拡張子}」で、同名があれば _2, _3 …（OutputFileNamer）。
/// </summary>
/// <remarks>
/// ファイルに書くのは、必ず ExportEncoder が作ったバイト列にする（表示した保存サイズと、保存したファイルのサイズを一致させるため。
/// 設計書 5.10-5）。保存のダイアログで同じ条件の保存サイズを計算してあれば、そのバイト列をそのまま書く（エンコードし直さない）。
/// バックグラウンドで呼んでよい（状態を持たない）。
/// </remarks>
public sealed class ExportService
{
    private readonly FormatRegistry _formats;
    private readonly ExportEncoder _encoder;
    private readonly TimeProvider _time;
    private readonly OutputPathFactory _createPath;

    /// <param name="formats">形式の登録</param>
    /// <param name="time">ファイル名に入れる日時（null なら今の日時。テストで差し替える）</param>
    /// <param name="createPath">保存するファイルのパスの決め方（null なら OutputFileNamer。テストで差し替える）</param>
    public ExportService(FormatRegistry formats, TimeProvider? time = null, OutputPathFactory? createPath = null)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = formats;
        _encoder = new ExportEncoder(formats);
        _time = time ?? TimeProvider.System;
        _createPath = createPath ?? new OutputFileNamer().CreatePath;
    }

    /// <summary>保存サイズの計算（保存のダイアログごとに 1 つ作る）</summary>
    public ExportPreviewService CreatePreviewService() => new(_formats);

    /// <summary>保存できる形式（登録順。Encode ができ、エンコーダーと拡張子がある形式。保存のダイアログの形式の一覧）</summary>
    public IReadOnlyList<IImageFormat> EncodableFormats
        => _formats.Encodable.Where(f => f.Encoder is not null && f.Extensions.Count > 0).ToList();

    /// <summary>
    /// 保存できる形式を探す（登録されていて、Encode ができる形式）
    /// </summary>
    /// <exception cref="UnsupportedImageFormatException">登録されていない、または保存できない形式</exception>
    public IImageFormat GetEncodableFormat(string formatId)
    {
        ArgumentException.ThrowIfNullOrEmpty(formatId);
        var format = _formats.FindById(formatId);
        if (format is null || !format.Capabilities.HasFlag(FormatCapabilities.Encode) || format.Encoder is null
            || format.Extensions.Count == 0)
        {
            throw new UnsupportedImageFormatException($"保存できない形式です: {formatId}", formatId, null);
        }
        return format;
    }

    /// <summary>
    /// 保存するファイルのパスを決める（まだ書かない。ダイアログに出すのにも使う）。拡張子は形式の先頭の拡張子
    /// </summary>
    /// <param name="outputFolder">出力先のフォルダ</param>
    /// <param name="sourcePath">元画像のパス（拡張子を除いた名前を使う）</param>
    /// <param name="formatId">保存する形式</param>
    public string CreateOutputPath(string outputFolder, string sourcePath, string formatId)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputFolder);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        var format = GetEncodableFormat(formatId);
        return _createPath(outputFolder, sourcePath, _time.GetLocalNow().DateTime, format.Extensions[0]);
    }

    /// <summary>
    /// 保存するファイル名（フォルダを除いたもの。ダイアログに出す）。出力先のフォルダがあれば、そこにあるファイルと
    /// 重ならない名前（_2 など）にする。出力先が未設定・無いときは、重なりを確かめない名前にする
    /// </summary>
    public string CreateOutputFileName(string? outputFolder, string sourcePath, string formatId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        if (!string.IsNullOrWhiteSpace(outputFolder) && Directory.Exists(outputFolder))
            return Path.GetFileName(CreateOutputPath(outputFolder, sourcePath, formatId));

        var format = GetEncodableFormat(formatId);
        var path = new OutputFileNamer(_ => false).CreatePath(".", sourcePath, _time.GetLocalNow().DateTime, format.Extensions[0]);
        return Path.GetFileName(path);
    }

    /// <summary>
    /// 保存する画像を作り、メモリ上にエンコードする（ファイルには書かない。保存サイズの計算と同じ処理）
    /// </summary>
    /// <param name="image">今の画像</param>
    /// <param name="settings">保存の設定</param>
    /// <param name="cancellationToken">取り消し</param>
    /// <exception cref="UnsupportedImageFormatException">保存できない形式</exception>
    public EncodedImage Encode(ImageBuffer image, ExportSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        var format = GetEncodableFormat(settings.FormatId);
        var exportImage = ExportRenderer.Render(image, settings, format.Capabilities.HasFlag(FormatCapabilities.Alpha),
            cancellationToken);
        return _encoder.Encode(exportImage, settings, cancellationToken);
    }

    /// <summary>
    /// 今の画像を保存する（設計書 F-10）。保存する画像を作ってエンコードし、そのバイト列を出力先に新しいファイルとして書く。
    /// 既にあるファイルは上書きせず、元画像（sourcePath）と同じパスには書かない
    /// </summary>
    /// <param name="image">今の画像（編集を確定した結果の全体）</param>
    /// <param name="sourcePath">元画像のパス（ファイル名に使い、書き込みから守る）</param>
    /// <param name="outputFolder">出力先のフォルダ（あること）</param>
    /// <param name="settings">保存の設定</param>
    /// <param name="cancellationToken">取り消し（書き始めた後は効かない）</param>
    /// <param name="precomputed">
    /// 保存のダイアログで計算した保存サイズの結果。image と settings が同じ条件のものなら、そのバイト列をそのまま書く
    /// （表示したサイズと、保存したファイルのサイズが一致する。設計書 5.6）。違う条件なら使わずにエンコードし直す
    /// </param>
    /// <exception cref="UnsupportedImageFormatException">保存できない形式</exception>
    /// <exception cref="InvalidOperationException">保存先が元画像と同じパスになった（書かない）</exception>
    /// <exception cref="IOException">保存先に既にファイルがある・書けない</exception>
    /// <exception cref="DirectoryNotFoundException">出力先のフォルダが無い</exception>
    public ExportResult Export(ImageBuffer image, string sourcePath, string outputFolder, ExportSettings settings,
        CancellationToken cancellationToken = default, ExportPreviewResult? precomputed = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(outputFolder);
        ArgumentNullException.ThrowIfNull(settings);

        var encoded = precomputed is { } computed && computed.Matches(image, settings)
            ? computed.Encoded
            : Encode(image, settings, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.GetFullPath(CreateOutputPath(outputFolder, sourcePath, settings.FormatId));
        SafeFileWriter.Write(path, encoded.Bytes, [sourcePath]);
        return new ExportResult(path, encoded);
    }
}
