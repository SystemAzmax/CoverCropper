namespace CoverCropper.Services;

/// <summary>%APPDATA%\CoverCropper\logs\app.log への簡易ログ（書き込みは共通の WebDeza.Core.AppLog）</summary>
public static class AppLog
{
    private static readonly WebDeza.Core.AppLog Log = new(AppPaths.LogFile);

    public static void Error(string context, Exception ex) => Log.Error(context, ex);

    public static void Write(string message) => Log.Write(message);
}
