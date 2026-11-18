using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.Exceptions;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>画面の状態を待つ。要素が消えた・まだ無いなどの一時的な例外は、待っている間は無視する</summary>
public static class Waits
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    /// <summary>get が null 以外を返すまで待つ。待ちきれなければ TimeoutException</summary>
    public static T For<T>(Func<T?> get, TimeSpan timeout, string what) where T : class
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            T? value = null;
            try { value = get(); }
            catch (Exception ex) when (IsTransient(ex)) { }
            if (value is not null) return value;
            if (watch.Elapsed > timeout) throw new TimeoutException($"待ちきれない（{timeout.TotalSeconds:0.#} 秒）: {what}");
            Thread.Sleep(Interval);
        }
    }

    /// <summary>条件が true になるまで待つ。待ちきれなければ false</summary>
    public static bool Until(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed <= timeout)
        {
            try
            {
                if (condition()) return true;
            }
            catch (Exception ex) when (IsTransient(ex)) { }
            Thread.Sleep(Interval);
        }
        return false;
    }

    public static bool IsTransient(Exception ex) =>
        ex is ElementNotAvailableException or PropertyNotSupportedException or PatternNotSupportedException
            or COMException or InvalidOperationException or TimeoutException;
}
