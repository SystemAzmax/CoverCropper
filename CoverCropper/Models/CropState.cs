namespace CoverCropper.Models;

/// <summary>
/// 読み込んだ画像の原寸・リサイズ後サイズと切り抜き枠位置。
/// 座標はすべてリサイズ後画像のピクセル座標（整数）。
/// </summary>
public sealed record CropState(
    string SourcePath,
    int OriginalWidth,
    int OriginalHeight,
    int ResizedWidth,
    int ResizedHeight)
{
    public bool IsUpscaled => ResizedWidth > OriginalWidth || ResizedHeight > OriginalHeight;
}
