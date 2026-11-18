using System.Runtime.ExceptionServices;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>
/// WPF の描画と WIC を STA のスレッドで使う。HEIF のコーデックは MTA のスレッドで使うと、
/// 例外にならずにプロセスごと落ちるため（実装計画書の 12-2）
/// </summary>
internal static class StaThread
{
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    public static T Run<T>(Func<T> func)
    {
        T result = default!;
        Run(() => { result = func(); });
        return result;
    }
}
