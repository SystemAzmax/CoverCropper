namespace WebDeza.Imaging.Formats;

/// <summary>形式でできること</summary>
[Flags]
public enum FormatCapabilities
{
    None = 0,

    /// <summary>読み込める（Decoder がある）</summary>
    Decode = 1,

    /// <summary>書き出せる（Encoder がある）</summary>
    Encode = 2,

    /// <summary>透過を保てる。書き出すとき、これが無い形式には背景色で塗った不透明な画像を渡す</summary>
    Alpha = 4,
}

/// <summary>
/// 画像の形式（入力・出力）。FormatRegistry に登録すると、「開く」の種類の一覧・D&amp;D の判定・
/// 書き出しの形式の一覧・オプションの入力欄が、登録内容から作られる。
/// </summary>
public interface IImageFormat
{
    /// <summary>"jpeg" など。設定・プリセットに書くので変えない</summary>
    string Id { get; }

    /// <summary>"JPEG" など。画面に出す名前</summary>
    string DisplayName { get; }

    /// <summary>".jpg" などの拡張子（ドット付き）。先頭が書き出すときの拡張子</summary>
    IReadOnlyList<string> Extensions { get; }

    FormatCapabilities Capabilities { get; }

    /// <summary>
    /// 読み書きに OS の拡張機能などが要る場合の説明（例: Microsoft Store の「HEIF 画像拡張機能」）。
    /// 読めなかったときに M21 として出す。要らない形式は null
    /// </summary>
    string? Requirement { get; }

    /// <summary>Capabilities に Decode があるときは必ずある</summary>
    IImageDecoder? Decoder { get; }

    /// <summary>Capabilities に Encode があるときは必ずある</summary>
    IImageEncoder? Encoder { get; }
}
