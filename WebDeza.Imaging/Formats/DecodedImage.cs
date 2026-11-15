namespace WebDeza.Imaging.Formats;

/// <summary>読み込んだ画像</summary>
/// <param name="Image">向きを直した Bgra32 の画像</param>
/// <param name="FormatId">読んだ形式の Id</param>
/// <param name="Metadata">読み込んだときの情報</param>
public sealed record DecodedImage(ImageBuffer Image, string FormatId, ImageMetadata Metadata);
