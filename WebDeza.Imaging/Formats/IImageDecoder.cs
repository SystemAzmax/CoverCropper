namespace WebDeza.Imaging.Formats;

/// <summary>形式の読み込み</summary>
public interface IImageDecoder
{
    /// <summary>
    /// 1 枚目（GIF は 1 フレーム目、TIFF は 1 ページ目など）を読み、向きを直した Bgra32 にする。
    /// stream は呼び出し側が閉じる。
    /// </summary>
    /// <exception cref="UnsupportedImageFormatException">この形式として読めない中身、または OS のコーデックが無い</exception>
    DecodedImage Decode(Stream stream, CancellationToken cancellationToken);

    /// <summary>
    /// 長辺が maxSize 以下のサムネイルを作る（縮小デコード・埋め込みサムネイルを使ってよい）。向きは直す。
    /// stream は呼び出し側が閉じる。
    /// </summary>
    /// <exception cref="UnsupportedImageFormatException">この形式として読めない中身、または OS のコーデックが無い</exception>
    ImageBuffer DecodeThumbnail(Stream stream, int maxSize, CancellationToken cancellationToken);
}
