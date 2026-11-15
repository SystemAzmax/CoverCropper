namespace WebDeza.Core;

/// <summary>
/// アプリごとのデータの場所（%APPDATA%\{アプリ名}）の組み立て。
/// CoverCropper と webDeza は、アプリ名を変えて別々のフォルダを使う。
/// </summary>
public static class AppPaths
{
    /// <summary>設定ファイルの名前</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>%APPDATA%\{アプリ名}</summary>
    public static string GetDataFolder(string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
    }

    /// <summary>%APPDATA%\{アプリ名}\logs\app.log</summary>
    public static string GetLogFile(string appName)
        => Path.Combine(GetDataFolder(appName), "logs", "app.log");

    /// <summary>%APPDATA%\{アプリ名}\settings.json</summary>
    public static string GetSettingsFile(string appName)
        => Path.Combine(GetDataFolder(appName), SettingsFileName);
}
