using System.Text;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Editing;

/// <summary>
/// 書き出し設定（全画像共通）。不変。
/// </summary>
/// <param name="FormatId">書き出す形式の Id</param>
/// <param name="FormatOptions">形式の Id → その形式のオプション（形式を切り替えても、それぞれの値を覚えておく）</param>
/// <param name="SizeLimitBytes">
/// ファイルサイズの上限（バイト）。形式のエンコーダーが SizeTuning を持つときだけ使う。null のときは「品質を指定」
/// </param>
/// <param name="Size">書き出す大きさ</param>
/// <param name="Background">透過を保てない形式で書き出すときに、透過部分を塗る色</param>
/// <remarks>比較は FormatOptions の中身で行う（書き出しプレビューの使い回しの判定に使う）。</remarks>
public sealed record ExportSettings(
    string FormatId,
    IReadOnlyDictionary<string, ParameterValues> FormatOptions,
    long? SizeLimitBytes,
    OutputSize Size,
    RgbColor Background)
{
    private readonly IReadOnlyDictionary<string, ParameterValues> _formatOptions =
        ParameterValuesMap.Freeze(FormatOptions, nameof(FormatOptions));

    /// <summary>形式の Id → オプション（複製して持つので、渡した辞書を後で変えても影響しない）</summary>
    public IReadOnlyDictionary<string, ParameterValues> FormatOptions
    {
        get => _formatOptions;
        init => _formatOptions = ParameterValuesMap.Freeze(value, nameof(FormatOptions));
    }

    /// <summary>形式のオプション。無ければ空（エンコーダーの既定値として扱われる）</summary>
    public ParameterValues GetOptions(string formatId)
        => _formatOptions.GetValueOrDefault(formatId) ?? ParameterValues.Empty;

    /// <summary>形式のオプションを 1 つ変えた設定</summary>
    public ExportSettings WithOptions(string formatId, ParameterValues options)
        => this with { FormatOptions = ParameterValuesMap.With(_formatOptions, formatId, options) };

    public bool Equals(ExportSettings? other)
        => other is not null
           && string.Equals(FormatId, other.FormatId, StringComparison.Ordinal)
           && SizeLimitBytes == other.SizeLimitBytes
           && Size == other.Size
           && Background == other.Background
           && ParameterValuesMap.ContentEquals(_formatOptions, other._formatOptions);

    public override int GetHashCode()
        => HashCode.Combine(FormatId, SizeLimitBytes, Size, Background, ParameterValuesMap.ContentHashCode(_formatOptions));

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"FormatId = {FormatId}, FormatOptions = {ParameterValuesMap.Format(_formatOptions)}, ");
        builder.Append($"SizeLimitBytes = {SizeLimitBytes?.ToString() ?? "null"}, Size = {Size}, Background = {Background}");
        return true;
    }
}
