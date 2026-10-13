namespace CoverCropper.Models;

/// <summary>%APPDATA%\CoverCropper\settings.json に保存される設定</summary>
public sealed class AppSettings
{
    /// <summary>切り抜き画像の保存先フォルダ</summary>
    public string? OutputFolder { get; set; }

    /// <summary>前回「開く」で使ったフォルダ</summary>
    public string? LastOpenFolder { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}
