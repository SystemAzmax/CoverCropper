using System.IO;

namespace CoverCropper.Services;

public static class AppPaths
{
    /// <summary>%APPDATA%\CoverCropper</summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoverCropper");

    /// <summary>%APPDATA%\CoverCropper\logs\app.log</summary>
    public static string LogFile { get; } = Path.Combine(DataFolder, "logs", "app.log");
}
