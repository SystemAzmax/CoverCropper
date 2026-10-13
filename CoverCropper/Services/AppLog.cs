using System.Globalization;
using System.IO;

namespace CoverCropper.Services;

/// <summary>%APPDATA%\CoverCropper\logs\app.log への簡易ログ</summary>
public static class AppLog
{
    private static readonly Lock Sync = new();

    public static void Error(string context, Exception ex)
        => Write($"[ERROR] {context}{Environment.NewLine}{ex}");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile)!);
                File.AppendAllText(AppPaths.LogFile,
                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ログ出力の失敗でアプリを止めない
        }
    }
}
