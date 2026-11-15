using System.Globalization;

namespace WebDeza.Core;

/// <summary>
/// ファイルへの簡易ログ。書き込み先はアプリが決めて渡す（AppPaths.GetLogFile）。
/// 1 件は「yyyy-MM-dd HH:mm:ss.fff メッセージ」の形で、ファイルの末尾に足す。
/// </summary>
public sealed class AppLog
{
    private readonly Lock _sync = new();
    private readonly TimeProvider _time;

    /// <param name="logFile">ログファイルのパス（フォルダが無ければ、書くときに作る）</param>
    public AppLog(string logFile) : this(logFile, TimeProvider.System) { }

    /// <param name="logFile">ログファイルのパス（フォルダが無ければ、書くときに作る）</param>
    /// <param name="time">時刻（テストで差し替える）</param>
    public AppLog(string logFile, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(logFile);
        ArgumentNullException.ThrowIfNull(time);
        LogFile = logFile;
        _time = time;
    }

    /// <summary>ログファイルのパス</summary>
    public string LogFile { get; }

    /// <summary>エラーを書く（[ERROR] 状況、改行、例外の内容）</summary>
    public void Error(string context, Exception ex)
        => Write($"[ERROR] {context}{Environment.NewLine}{ex}");

    /// <summary>1 件書く。書けなくても例外は投げない（ログ出力の失敗でアプリを止めない）</summary>
    public void Write(string message)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                var now = _time.GetLocalNow().DateTime;
                File.AppendAllText(LogFile,
                    $"{now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ログ出力の失敗でアプリを止めない
        }
    }
}
