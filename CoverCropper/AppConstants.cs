using System.IO;

namespace CoverCropper;

/// <summary>アプリ全体の固定値。出力サイズや品質を変える場合はここだけを修正する。</summary>
public static class AppConstants
{
    public const int TargetWidth = 1024;
    public const int TargetHeight = 768;
    public const int JpegQuality = 85;

    /// <summary>矢印キー1回の移動量（リサイズ後画像のピクセル）</summary>
    public const int KeyMoveStep = 1;

    /// <summary>Shift+矢印キー1回の移動量（リサイズ後画像のピクセル）</summary>
    public const int KeyMoveStepLarge = 10;

    public static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    public static bool IsSupportedFile(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext)
            && SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }
}
