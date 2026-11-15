namespace CoverCropper.Services;

/// <summary>CoverCropper のデータの場所。組み立ては共通の WebDeza.Core.AppPaths に任せる</summary>
public static class AppPaths
{
    private const string AppName = "CoverCropper";

    /// <summary>%APPDATA%\CoverCropper</summary>
    public static string DataFolder { get; } = WebDeza.Core.AppPaths.GetDataFolder(AppName);

    /// <summary>%APPDATA%\CoverCropper\logs\app.log</summary>
    public static string LogFile { get; } = WebDeza.Core.AppPaths.GetLogFile(AppName);
}
