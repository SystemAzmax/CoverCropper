namespace WebDeza.Imaging.Formats;

/// <summary>読み込んだときの情報</summary>
/// <param name="AppliedExifOrientation">読み込み時に適用した EXIF Orientation（1〜8。向きの情報が無いときは 1）</param>
/// <param name="FrameCount">ファイルに入っていたフレーム（ページ）の数。使うのは 1 枚目だけ</param>
public sealed record ImageMetadata(int AppliedExifOrientation, int FrameCount);
