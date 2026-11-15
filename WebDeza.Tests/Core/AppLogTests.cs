using System.IO;
using WebDeza.Core;

namespace WebDeza.Tests.Core;

public class AppLogTests
{
    private static readonly DateTimeOffset Time = new(2026, 11, 15, 9, 5, 7, 42, TimeSpan.Zero);

    [Fact]
    public void Write_AppendsLineWithTimestamp_AndCreatesFolder()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("logs", "app.log"); // logs フォルダはまだ無い
        var time = new ManualTimeProvider(Time);
        var log = new AppLog(path, time);

        log.Write("first");
        time.Now = Time.AddSeconds(1);
        log.Write("second");

        var nl = Environment.NewLine;
        Assert.Equal($"2026-11-15 09:05:07.042 first{nl}2026-11-15 09:05:08.042 second{nl}", File.ReadAllText(path));
        Assert.Equal(path, log.LogFile);
    }

    [Fact]
    public void Error_WritesContextAndException()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("app.log");
        var log = new AppLog(path, new ManualTimeProvider(Time));
        var ex = new InvalidOperationException("boom");

        log.Error("Load failed: a.jpg", ex);

        var nl = Environment.NewLine;
        Assert.Equal($"2026-11-15 09:05:07.042 [ERROR] Load failed: a.jpg{nl}{ex}{nl}", File.ReadAllText(path));
    }

    [Fact]
    public void Write_UsesInvariantDigitsRegardlessOfCulture()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("app.log");
        var log = new AppLog(path, new ManualTimeProvider(Time));
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            log.Write("x");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }

        Assert.StartsWith("2026-11-15 09:05:07.042 x", File.ReadAllText(path));
    }

    [Fact]
    public void Write_WhenFileCannotBeWritten_DoesNotThrow()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("app.log");
        var log = new AppLog(path);
        log.Write("before");

        // ほかのプロセスが開いていて書けない状態
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            log.Write("while locked");
        }

        // ログのパスにフォルダがあって書けない状態
        var blocked = temp.Combine("blocked.log");
        Directory.CreateDirectory(blocked);
        new AppLog(blocked).Write("x");

        Assert.Contains("before", File.ReadAllText(path));
        Assert.DoesNotContain("while locked", File.ReadAllText(path));
    }

    [Fact]
    public void Write_FromManyThreads_KeepsEveryLine()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("app.log");
        var log = new AppLog(path, new ManualTimeProvider(Time));

        Parallel.For(0, 200, i => log.Write($"line {i}"));

        var lines = File.ReadAllLines(path);
        Assert.Equal(200, lines.Length);
        Assert.All(lines, line => Assert.StartsWith("2026-11-15 09:05:07.042 line ", line));
        Assert.Equal(200, lines.Distinct().Count());
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => new AppLog(""));
        Assert.Throws<ArgumentNullException>(() => new AppLog("a.log", null!));
    }
}
