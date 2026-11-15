namespace WebDeza.Imaging.Formats;

/// <summary>
/// 対応していない形式、またはデコードできない中身。
/// NotSupportedException の派生なので、CoverCropper の「NotSupportedException なら M01」の判定がそのまま使える。
/// </summary>
public sealed class UnsupportedImageFormatException : NotSupportedException
{
    public const string DefaultMessage = "対応していないファイル形式です。";

    public UnsupportedImageFormatException()
        : this(DefaultMessage)
    {
    }

    public UnsupportedImageFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <param name="message">内容</param>
    /// <param name="formatId">読もうとした形式の Id（分からなければ null）</param>
    /// <param name="requirement">読むのに要るもの（M21 に出す。無ければ null）</param>
    /// <param name="innerException">元の例外</param>
    public UnsupportedImageFormatException(string message, string? formatId, string? requirement,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FormatId = formatId;
        Requirement = requirement;
    }

    /// <summary>読もうとした形式の Id（拡張子が登録されていないときなど、分からなければ null）</summary>
    public string? FormatId { get; }

    /// <summary>この形式を読むのに要るもの（M21 に出す）。無ければ null</summary>
    public string? Requirement { get; }
}
