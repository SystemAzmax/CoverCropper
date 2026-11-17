using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Imaging.Wic;
using WebDeza.Services;

namespace WebDeza.Tests.Services;

/// <summary>一括保存の処理（設計書 F-11・5.6、V08〜V11。実装計画書 10-10）</summary>
public sealed class BatchExporterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly RgbColor White = new(255, 255, 255);

    private readonly TempFolder _temp = new();
    private readonly FormatRegistry _formats = new();
    private readonly FakeEncoderFormat _fake = new("fakejpeg", alpha: false, ".fjpg");
    private readonly string _output;
    private readonly BatchExporter _exporter;

    public BatchExporterTests()
    {
        _formats.AddWicFormats();
        _formats.Register(_fake);
        _output = _temp.Combine("out");
        Directory.CreateDirectory(_output);
        _exporter = new BatchExporter(new ExportService(_formats, new ManualTimeProvider(Now)), new AppLog(LogFile));
    }

    public void Dispose() => _temp.Dispose();

    private string LogFile => _temp.Combine("logs", "app.log");

    /// <summary>進み具合を、届いた順に記録する（Progress&lt;T&gt; と違い、すぐ記録する）</summary>
    private sealed class RecordingProgress : IProgress<BatchProgress>
    {
        public List<BatchProgress> Reports { get; } = new();

        public void Report(BatchProgress value)
        {
            lock (Reports) Reports.Add(value);
        }
    }

    private BatchJob Job(string name, int width = 8, int height = 4)
    {
        var path = _temp.Combine(name);
        File.WriteAllBytes(path, [1, 2, 3]);
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 4 == 3 ? 255 : i * 7 % 251);
        return new BatchJob(path, ImageBuffer.Create(width, height, pixels));
    }

    private static ExportSettings Settings(string formatId, long? sizeLimit = null)
        => new(formatId, new Dictionary<string, ParameterValues>(), sizeLimit, White);

    [Fact]
    public async Task AllSucceed_SavesInOrder_AndReportsProgress()
    {
        var jobs = new[] { Job("a.png"), Job("b.png"), Job("c.png") };
        var progress = new RecordingProgress();

        var result = await _exporter.RunAsync(jobs, _output, Settings(_fake.Id), progress);

        Assert.Equal(["a.png", "b.png", "c.png"], result.Items.Select(i => i.FileName));
        Assert.All(result.Items, i => Assert.True(i.Succeeded));
        Assert.Equal(["a_20261118_090000.fjpg", "b_20261118_090000.fjpg", "c_20261118_090000.fjpg"],
            result.Items.Select(i => Path.GetFileName(i.SavedPath!)));
        Assert.Equal((3, 0), (result.SucceededCount, result.FailedCount));
        Assert.False(result.Cancelled);
        Assert.False(result.OutputFolderMissing);
        Assert.Equal([1, 2, 3], progress.Reports.Select(p => p.Done));
        Assert.All(progress.Reports, p => Assert.Equal(3, p.Total));
        Assert.Equal(result.Items, progress.Reports.Select(p => p.Last));
        Assert.All(jobs, j => Assert.Equal([1, 2, 3], File.ReadAllBytes(j.SourcePath))); // 元画像は変わらない
    }

    [Fact]
    public async Task SecondEncodeFails_OthersAreSaved_AndRunsToTheEnd()
    {
        // V08: 3 件中 2 件目のエンコードが失敗する → 1・3 件目は保存、2 件目はエラー、最後まで進む
        int calls = 0;
        _fake.OnEncode = () =>
        {
            if (Interlocked.Increment(ref calls) == 2) throw new InvalidOperationException("壊れた");
        };

        var result = await _exporter.RunAsync([Job("a.png"), Job("b.png"), Job("c.png")], _output, Settings(_fake.Id));

        Assert.Equal(3, result.Items.Count);
        Assert.True(result.Items[0].Succeeded);
        Assert.False(result.Items[1].Succeeded);
        Assert.Equal("壊れた", result.Items[1].Error);
        Assert.Null(result.Items[1].SavedPath);
        Assert.True(result.Items[2].Succeeded);
        Assert.Equal((2, 1), (result.SucceededCount, result.FailedCount));
        Assert.False(result.OutputFolderMissing);
        Assert.Equal(2, Directory.GetFiles(_output).Length);
        Assert.Contains("Batch export failed", File.ReadAllText(LogFile)); // ログに書く
        Assert.Equal(result.Items[0].Bytes + result.Items[2].Bytes, result.TotalBytes);
    }

    [Fact]
    public async Task CancelWhileSavingFirst_FinishesFirst_AndStops()
    {
        // V09: 1 件目の保存中に中止 → 1 件目は保存、2 件目以降は処理せず Cancelled
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        _fake.OnEncode = () =>
        {
            if (Interlocked.Increment(ref calls) == 1) cancellation.Cancel(); // 1 件目の保存中に［中止］
        };

        var result = await _exporter.RunAsync([Job("a.png"), Job("b.png"), Job("c.png")], _output, Settings(_fake.Id),
            null, cancellation.Token);

        Assert.True(result.Cancelled);
        var item = Assert.Single(result.Items);
        Assert.True(item.Succeeded); // 保存中の 1 件は最後まで終える
        Assert.True(File.Exists(item.SavedPath));
        Assert.Equal(1, calls); // 2 件目以降はエンコードもしない
        Assert.Single(Directory.GetFiles(_output));
    }

    [Fact]
    public async Task CancelledBeforeStart_SavesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await _exporter.RunAsync([Job("a.png")], _output, Settings(_fake.Id), null, cancellation.Token);

        Assert.True(result.Cancelled);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalBytes);
    }

    [Fact]
    public async Task FolderGoneOnSecondFailure_Stops()
    {
        // V10: 2 件目の失敗時に出力先フォルダが無い → そこで止まり OutputFolderMissing
        int calls = 0;
        _fake.OnEncode = () =>
        {
            if (Interlocked.Increment(ref calls) == 2) Directory.Delete(_output, recursive: true); // 2 件目の保存中に消された
        };

        var result = await _exporter.RunAsync([Job("a.png"), Job("b.png"), Job("c.png")], _output, Settings(_fake.Id));

        Assert.True(result.OutputFolderMissing);
        Assert.False(result.Cancelled);
        Assert.Equal(2, result.Items.Count); // 3 件目は処理しない
        Assert.True(result.Items[0].Succeeded);
        Assert.False(result.Items[1].Succeeded);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailureWithFolderPresent_DoesNotStop()
    {
        // 失敗しても、出力先フォルダがあれば止めない（V08 と同じ。名前が重なって書けないなど）
        var taken = Path.Combine(_output, "taken.fjpg");
        File.WriteAllBytes(taken, [9]);
        int count = 0;
        var exporter = new BatchExporter(
            new ExportService(_formats, new ManualTimeProvider(Now),
                (folder, source, now, ext) => Interlocked.Increment(ref count) == 1 ? taken : new OutputFileNamer().CreatePath(folder, source, now, ext)),
            new AppLog(LogFile));

        var result = await exporter.RunAsync([Job("a.png"), Job("b.png")], _output, Settings(_fake.Id));

        Assert.False(result.Items[0].Succeeded); // 既にあるファイルは上書きしない
        Assert.True(result.Items[1].Succeeded);
        Assert.False(result.OutputFolderMissing);
        Assert.Equal([9], File.ReadAllBytes(taken));
    }

    [Fact]
    public async Task SizeLimit_EachFileWithinLimit_AndTotalMatches()
    {
        // V11: 上限 300 KB で 3 件 → 3 件とも 300 KB 以下。TotalBytes が各ファイルの合計と一致
        BatchJob Noisy(string name, int seed)
        {
            var path = _temp.Combine(name);
            File.WriteAllBytes(path, [1]);
            var pixels = new byte[600 * 450 * 4];
            new Random(seed).NextBytes(pixels);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return new BatchJob(path, ImageBuffer.Create(600, 450, pixels));
        }
        const long limit = 300 * 1024;

        var result = await _exporter.RunAsync([Noisy("a.png", 1), Noisy("b.png", 2), Noisy("c.png", 3)], _output,
            Settings(WicFormats.JpegId, limit));

        Assert.Equal(3, result.SucceededCount);
        var sizes = result.Items.Select(i => new FileInfo(i.SavedPath!).Length).ToList();
        Assert.All(sizes, s => Assert.True(s <= limit, $"{s} > {limit}"));
        Assert.Equal(sizes.Sum(), result.TotalBytes);
        Assert.Equal(sizes, result.Items.Select(i => i.Bytes));
    }

    [Fact]
    public async Task EmptyJobs_ReturnsEmptyResult()
    {
        var result = await _exporter.RunAsync([], _output, Settings(_fake.Id));

        Assert.Empty(result.Items);
        Assert.False(result.Cancelled);
        Assert.Equal(0, result.TotalBytes);
    }
}
