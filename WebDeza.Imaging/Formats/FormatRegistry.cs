namespace WebDeza.Imaging.Formats;

/// <summary>
/// 形式の登録先。拡張子から形式を選び、元ファイルを読み込む。
/// 元ファイルを開くのはこのクラスだけとする（元画像を変えないため。設計書 1.4）。
/// </summary>
public sealed class FormatRegistry
{
    private readonly List<IImageFormat> _formats = new();
    private readonly Dictionary<string, IImageFormat> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>登録順の一覧</summary>
    public IReadOnlyList<IImageFormat> All => _formats;

    /// <summary>読み込める形式（登録順）。「開く」の種類の一覧と D&amp;D の判定に使う</summary>
    public IReadOnlyList<IImageFormat> Decodable
        => _formats.Where(f => f.Capabilities.HasFlag(FormatCapabilities.Decode)).ToArray();

    /// <summary>書き出せる形式（登録順）。書き出しの形式の一覧に使う</summary>
    public IReadOnlyList<IImageFormat> Encodable
        => _formats.Where(f => f.Capabilities.HasFlag(FormatCapabilities.Encode)).ToArray();

    /// <summary>形式を登録する</summary>
    /// <exception cref="ArgumentException">
    /// 同じ Id・同じ拡張子（大文字小文字は区別しない）が登録済み、または記述が正しくない
    /// （Id が空、拡張子が無いか "." で始まらない、Decode / Encode があるのに Decoder / Encoder が無い）
    /// </exception>
    public void Register(IImageFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        Validate(format);

        if (FindById(format.Id) is not null)
            throw new ArgumentException($"Id「{format.Id}」の形式は登録済みです。", nameof(format));

        foreach (var extension in format.Extensions)
        {
            if (_byExtension.TryGetValue(extension, out var other))
                throw new ArgumentException(
                    $"拡張子「{extension}」は形式「{other.Id}」に登録済みです。", nameof(format));
        }

        _formats.Add(format);
        foreach (var extension in format.Extensions)
            _byExtension.Add(extension, format);
    }

    /// <summary>Id で形式を探す（大文字小文字を区別する）。無ければ null</summary>
    public IImageFormat? FindById(string id)
        => _formats.Find(f => string.Equals(f.Id, id, StringComparison.Ordinal));

    /// <summary>拡張子（".jpg" または "jpg"。大文字小文字は区別しない）で形式を探す。無ければ null</summary>
    public IImageFormat? FindByExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return null;
        if (extension[0] != '.') extension = "." + extension;
        return _byExtension.GetValueOrDefault(extension);
    }

    /// <summary>パスの拡張子が、読み込める形式に登録されているか（ファイルがあるかどうかは見ない）</summary>
    public bool CanDecode(string path)
        => FindDecodable(path) is not null;

    /// <summary>
    /// 元ファイルを読み込む。ファイルは FileMode.Open・FileAccess.Read・FileShare.Read で開き、読み終えたらすぐ閉じる。
    /// </summary>
    /// <exception cref="UnsupportedImageFormatException">
    /// 拡張子が読み込める形式に登録されていない、またはその形式として読めない（Requirement があれば例外に入れる）
    /// </exception>
    public DecodedImage Decode(string path, CancellationToken cancellationToken = default)
    {
        var format = GetDecodable(path);
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = OpenSource(path);
        return CallDecoder(format, () => format.Decoder!.Decode(stream, cancellationToken));
    }

    /// <summary>
    /// 元ファイルから、長辺が maxSize 以下のサムネイルを作る。ファイルの開き方は Decode と同じ。
    /// </summary>
    /// <exception cref="UnsupportedImageFormatException">Decode と同じ</exception>
    public ImageBuffer DecodeThumbnail(string path, int maxSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSize);
        var format = GetDecodable(path);
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = OpenSource(path);
        return CallDecoder(format, () => format.Decoder!.DecodeThumbnail(stream, maxSize, cancellationToken));
    }

    // ---------------------------------------------------------------- 内部

    private IImageFormat? FindDecodable(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var format = FindByExtension(Path.GetExtension(path));
        return format is not null && format.Capabilities.HasFlag(FormatCapabilities.Decode) ? format : null;
    }

    private IImageFormat GetDecodable(string path)
        => FindDecodable(path) ?? throw new UnsupportedImageFormatException(
            UnsupportedImageFormatException.DefaultMessage, FindByExtension(Path.GetExtension(path))?.Id, null);

    /// <summary>元画像を変えないよう、読み取り専用で開く。書き込もうとするほかのプロセスとは共有しない</summary>
    private static FileStream OpenSource(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>
    /// デコーダーを呼ぶ。デコーダーが UnsupportedImageFormatException を投げ、要るもの（Requirement）が入っていないときは、
    /// 形式の Requirement を入れて投げ直す（M21 を出せるようにする）
    /// </summary>
    private static T CallDecoder<T>(IImageFormat format, Func<T> decode)
    {
        try
        {
            return decode();
        }
        catch (UnsupportedImageFormatException ex)
            when (ex.Requirement is null && (format.Requirement is not null || ex.FormatId is null))
        {
            throw new UnsupportedImageFormatException(ex.Message, ex.FormatId ?? format.Id, format.Requirement, ex);
        }
    }

    private static void Validate(IImageFormat format)
    {
        if (string.IsNullOrEmpty(format.Id))
            throw new ArgumentException("形式の Id が空です。", nameof(format));

        if (format.Extensions is not { Count: > 0 })
            throw new ArgumentException($"形式「{format.Id}」に拡張子がありません。", nameof(format));

        foreach (var extension in format.Extensions)
        {
            if (extension is not { Length: >= 2 } || extension[0] != '.')
                throw new ArgumentException(
                    $"形式「{format.Id}」の拡張子「{extension}」は、\".jpg\" のように \".\" で始めてください。", nameof(format));
        }

        if (format.Extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != format.Extensions.Count)
            throw new ArgumentException($"形式「{format.Id}」の拡張子が重複しています。", nameof(format));

        if (format.Capabilities.HasFlag(FormatCapabilities.Decode) && format.Decoder is null)
            throw new ArgumentException($"形式「{format.Id}」は読み込めることになっていますが、Decoder がありません。", nameof(format));

        if (format.Capabilities.HasFlag(FormatCapabilities.Encode) && format.Encoder is null)
            throw new ArgumentException($"形式「{format.Id}」は書き出せることになっていますが、Encoder がありません。", nameof(format));
    }
}
