using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Services;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>一括保存の画面（設計書 F-11・4.6、M11・M12。実装計画書 10-11）</summary>
public sealed class MainViewModelBatchTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly FakeFormat _fakeDecoder = new("fake", ".fake");
    private readonly FakeEncoderFormat _fakeEncoder = new("fakejpeg", alpha: false, ".fjpg");
    private readonly FormatRegistry _formats = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly string _settingsPath;
    private readonly string _output;
    private readonly MainViewModel _vm;

    public MainViewModelBatchTests()
    {
        _formats.AddWicFormats();
        _formats.Register(_fakeDecoder);
        _formats.Register(_fakeEncoder);
        _settingsPath = _temp.Combine("settings.json");
        _output = _temp.Combine("out");
        Directory.CreateDirectory(_output);
        _vm = new MainViewModel(_formats, new SettingsService(_formats, _settingsPath), _dialogs,
            new AppLog(_temp.Combine("logs", "app.log")),
            exporter: new ExportService(_formats, new ManualTimeProvider(Now)));
        _vm.Settings.OutputFolder = _output;
        _vm.Settings.Export.FormatId = _fakeEncoder.Id;
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>a.fake・b.fake・c.fake を開き、どれにも編集を 1 つ確定させる（保存していない編集がある状態）</summary>
    private async Task<IReadOnlyList<DocumentViewModel>> OpenEditedAsync(params string[] names)
    {
        if (names.Length == 0) names = ["a.fake", "b.fake", "c.fake"];
        var paths = names.Select(n => { var p = _temp.Combine(n); File.WriteAllBytes(p, [1, 2, 3]); return p; }).ToList();
        await _vm.OpenPathsAsync(paths);
        foreach (var document in _vm.Documents)
        {
            await document.ApplyEditAsync("編集", (image, _) => ImageBuffer.Create(image.Width, image.Height, new byte[image.Pixels.Length]),
                changedRegion: null, newSelection: null, resetView: false);
        }
        return _vm.Documents.ToList();
    }

    private ExportDialogViewModel OpenBatchDialog()
    {
        _vm.ExportAllCommand.Execute(null);
        var dialog = _dialogs.ExportDialogs[^1];
        return dialog;
    }

    /// <summary>
    /// 一括保存のダイアログを開き、保存サイズの計算（これもエンコードを呼ぶ）が終わるのを待つ。
    /// エンコードの回数で細工を仕掛けるテストは、この後に仕掛ける
    /// </summary>
    private async Task<ExportDialogViewModel> OpenBatchDialogAndWaitAsync()
    {
        var dialog = OpenBatchDialog();
        await dialog.WaitForSizeAsync();
        return dialog;
    }

    // ---------------------------------------------------------------- 開く

    [Fact]
    public async Task ExportAll_EnabledWithImages_AndNotWhileBusy()
    {
        Assert.False(_vm.ExportAllCommand.CanExecute(null));
        var documents = await OpenEditedAsync();
        Assert.True(_vm.ExportAllCommand.CanExecute(null));

        var gate = new ManualResetEventSlim();
        var editing = documents[0].ApplyEditAsync("編集", (image, _) => { gate.Wait(TimeSpan.FromSeconds(10)); return image; },
            changedRegion: null, newSelection: null, resetView: false);
        Assert.False(_vm.ExportAllCommand.CanExecute(null));
        gate.Set();
        await editing;
    }

    [Fact]
    public async Task ExportAll_OpensBatchForm_ForActiveDocument()
    {
        var documents = await OpenEditedAsync();
        _vm.ActiveDocument = documents[1];

        var dialog = OpenBatchDialog();

        Assert.True(dialog.IsBatch);
        Assert.Equal(3, dialog.BatchCount);
        Assert.Equal("すべて保存（3 件）", dialog.Title);
        Assert.Equal("3 件を保存", dialog.SaveButtonText);
        Assert.Same(documents[1], dialog.Document); // プレビューと保存サイズはアクティブな画像のもの
        Assert.Equal("b_20261118_090000.fjpg ほか 2 件", dialog.FileName);
        Assert.False(dialog.IsBatchRunning);
        dialog.OnClosed();
    }

    [Fact]
    public async Task SingleExport_IsNotBatchForm()
    {
        await OpenEditedAsync();
        _vm.ExportCommand.Execute(null);
        var dialog = _dialogs.ExportDialogs[^1];

        Assert.False(dialog.IsBatch);
        Assert.Equal("保存", dialog.SaveButtonText);
        Assert.StartsWith("保存: ", dialog.Title);
        dialog.OnClosed();
    }

    // ---------------------------------------------------------------- 実行

    [Fact]
    public async Task Run_SavesAll_ClearsMarks_AndShowsM11()
    {
        // 追加: 保存できた画像の * が消える
        var documents = await OpenEditedAsync();
        var dialog = OpenBatchDialog();
        var events = new List<string>();
        dialog.CloseRequested += (_, _) => events.Add("close");
        dialog.BatchFinished += (_, _) => events.Add("finished");
        var running = new List<bool>();
        dialog.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ExportDialogViewModel.IsBatchRunning)) running.Add(dialog.IsBatchRunning); };

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["close", "finished"], events); // ダイアログを閉じてから、結果を知らせる
        Assert.Equal([true, false], running);
        Assert.Equal(3, Directory.GetFiles(_output).Length);
        Assert.All(documents, d => Assert.False(d.HasUnexportedEdits));
        Assert.All(documents, d => Assert.Equal(1, d.ExportCount));
        var outcome = dialog.BatchOutcome!;
        Assert.Equal(3, outcome.SucceededCount);
        var m11 = Messages.M11_BatchFinished(3, 0, FileSizeFormatter.Format(outcome.TotalBytes));
        Assert.Equal([m11], _dialogs.Infos);   // 失敗が無ければ情報
        Assert.Empty(_dialogs.Warnings);
        Assert.Equal(m11, _vm.StatusMessage);
        Assert.Equal(_output, new SettingsService(_formats, _settingsPath).Load().Settings.OutputFolder); // 設定に記録する
    }

    [Fact]
    public async Task Run_WithFailure_ShowsWarningWithFileNames()
    {
        var documents = await OpenEditedAsync();
        var dialog = await OpenBatchDialogAndWaitAsync();
        int calls = 0;
        _fakeEncoder.OnEncode = () => { if (Interlocked.Increment(ref calls) == 2) throw new InvalidOperationException("壊れた"); };

        await dialog.SaveCommand.ExecuteAsync(null);

        var warning = Assert.Single(_dialogs.Warnings); // 失敗があれば警告で、失敗したファイル名を並べる
        Assert.StartsWith(Messages.M11_BatchFinished(2, 1, FileSizeFormatter.Format(dialog.BatchOutcome!.TotalBytes)), warning);
        Assert.Contains("・b.fake（壊れた）", warning);
        Assert.Empty(_dialogs.Infos);
        Assert.False(documents[0].HasUnexportedEdits);
        Assert.True(documents[1].HasUnexportedEdits); // 失敗した画像の印は残る
        Assert.False(documents[2].HasUnexportedEdits);
    }

    [Fact]
    public async Task Cancel_StopsAfterCurrent_AndShowsM12()
    {
        // 追加: 中止で M12
        var documents = await OpenEditedAsync();
        var dialog = await OpenBatchDialogAndWaitAsync();
        int calls = 0;
        _fakeEncoder.OnEncode = () =>
        {
            if (Interlocked.Increment(ref calls) == 1) dialog.CancelBatchCommand.Execute(null); // 1 件目の保存中に［中止］
        };
        Assert.False(dialog.CancelBatchCommand.CanExecute(null)); // 実行中だけ押せる

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.BatchOutcome!.Cancelled);
        Assert.Equal(Messages.M12_BatchCancelled(1), _vm.StatusMessage);
        Assert.Empty(_dialogs.Infos); // M12 はステータスバーだけ
        Assert.Empty(_dialogs.Warnings);
        Assert.False(documents[0].HasUnexportedEdits); // 保存中の 1 件は最後まで終える
        Assert.True(documents[1].HasUnexportedEdits);
        Assert.True(documents[2].HasUnexportedEdits);
        Assert.Single(Directory.GetFiles(_output));
    }

    [Fact]
    public async Task FolderGoneDuringRun_ShowsM06()
    {
        await OpenEditedAsync();
        var dialog = await OpenBatchDialogAndWaitAsync();
        int calls = 0;
        _fakeEncoder.OnEncode = () => { if (Interlocked.Increment(ref calls) == 2) Directory.Delete(_output, recursive: true); };

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.BatchOutcome!.OutputFolderMissing);
        Assert.Equal([Messages.M06_OutputFolderNotFound(_output)], _dialogs.Errors);
        Assert.Equal(Messages.M11_BatchFinished(1, 1, FileSizeFormatter.Format(dialog.BatchOutcome.TotalBytes)), _vm.StatusMessage);
        Assert.Equal(2, calls); // 3 件目は処理しない
    }

    [Fact]
    public async Task FolderNotFoundBeforeStart_DoesNotRun()
    {
        // 出力先は始める前に一度だけ確かめる（F-10 と同じ）。無ければ M06 で、ダイアログは閉じない
        var documents = await OpenEditedAsync();
        var missing = _temp.Combine("missing");
        _vm.Settings.OutputFolder = missing;
        var dialog = OpenBatchDialog();
        int closeRequested = 0;
        dialog.CloseRequested += (_, _) => closeRequested++;

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal([Messages.M06_OutputFolderNotFound(missing)], _dialogs.Errors);
        Assert.Null(dialog.BatchOutcome);
        Assert.Equal(0, closeRequested);
        Assert.All(documents, d => Assert.True(d.HasUnexportedEdits));
        dialog.OnClosed();
    }

    [Fact]
    public async Task FolderNotSet_AsksM05_ThenRuns()
    {
        await OpenEditedAsync();
        _vm.Settings.OutputFolder = null;
        _dialogs.FolderToSelect = _output;
        var dialog = OpenBatchDialog();

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal([Messages.M05_OutputFolderNotSet], _dialogs.OkCancelConfirmations);
        Assert.Equal(3, dialog.BatchOutcome!.SucceededCount);
        Assert.Equal(_output, _vm.Settings.OutputFolder);
    }

    [Fact]
    public async Task ClosedWithoutRunning_RecordsNothing()
    {
        var documents = await OpenEditedAsync();
        _vm.SaveSettingsQuietly();
        var before = File.ReadAllText(_settingsPath);

        OpenBatchDialog().OnClosed(); // 実行せずに閉じた

        Assert.Equal(before, File.ReadAllText(_settingsPath));
        Assert.All(documents, d => Assert.True(d.HasUnexportedEdits));
        Assert.Empty(_dialogs.Infos);
    }

    [Fact]
    public void BatchFinishedMessage_ListsFailures()
    {
        var result = new BatchResult(
        [
            new BatchItemResult(@"C:\images\a.jpg", @"D:\out\a.jpg", 1024, null),
            new BatchItemResult(@"C:\images\b.jpg", null, 0, "書けません"),
            new BatchItemResult(@"C:\images\c.jpg", null, 0, "壊れた"),
        ], Cancelled: false, OutputFolderMissing: false, TotalBytes: 1024);

        Assert.Equal("一括保存が終わりました。成功 1 件 ／ 失敗 2 件 ／ 合計 1.0 KB\n\n失敗した画像:\n・b.jpg（書けません）\n・c.jpg（壊れた）",
            MainViewModel.BatchFinishedMessage(result));
        Assert.Equal("一括保存が終わりました。成功 1 件 ／ 失敗 0 件 ／ 合計 1.0 KB",
            MainViewModel.BatchFinishedMessage(result with { Items = [result.Items[0]] }));
    }
}
