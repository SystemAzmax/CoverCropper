using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Services;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>保存のダイアログを開き、保存した後（設計書 F-08・F-10、V32。実装計画書 10-2）</summary>
public sealed class MainViewModelExportTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 30, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly FakeFormat _fake = new("fake", ".fake");
    private readonly FormatRegistry _formats = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly string _settingsPath;
    private readonly string _output;
    private readonly MainViewModel _vm;

    public MainViewModelExportTests()
    {
        _formats.AddWicFormats();
        _formats.Register(_fake);
        _settingsPath = _temp.Combine("settings.json");
        _output = _temp.Combine("out");
        Directory.CreateDirectory(_output);
        _vm = new MainViewModel(_formats, new SettingsService(_formats, _settingsPath), _dialogs,
            new AppLog(_temp.Combine("logs", "app.log")),
            exporter: new ExportService(_formats, new ManualTimeProvider(Now)));
    }

    public void Dispose() => _temp.Dispose();

    private async Task<DocumentViewModel> OpenAsync(string name = "a.fake")
    {
        var path = _temp.Combine(name);
        File.WriteAllBytes(path, new byte[16]);
        await _vm.LoadAsync(path);
        return _vm.ActiveDocument!;
    }

    private static Task EditAsync(DocumentViewModel document)
        => document.ApplyEditAsync("編集", (image, _) => ImageBuffer.Create(image.Width, image.Height, new byte[image.Pixels.Length]),
            changedRegion: null, newSelection: null, resetView: false);

    /// <summary>保存のダイアログを開いて返す（FakeDialogs は開いたものを覚えるだけで、すぐ戻る）</summary>
    private ExportDialogViewModel OpenDialog()
    {
        _vm.ExportCommand.Execute(null);
        return _dialogs.ExportDialogs[^1];
    }

    [Fact]
    public async Task Export_EnabledOnlyWithImage_AndNotWhileBusy()
    {
        Assert.False(_vm.ExportCommand.CanExecute(null));
        var document = await OpenAsync();
        Assert.True(_vm.ExportCommand.CanExecute(null));

        var gate = new ManualResetEventSlim();
        var editing = document.ApplyEditAsync("編集", (image, _) => { gate.Wait(TimeSpan.FromSeconds(10)); return image; },
            changedRegion: null, newSelection: null, resetView: false);
        Assert.False(_vm.ExportCommand.CanExecute(null)); // 確定中は保存を受け付けない
        gate.Set();
        await editing;
        Assert.True(_vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task Export_OpensDialogForActiveDocument_WithSavedSettings()
    {
        _vm.Settings.OutputFolder = _output;
        var document = await OpenAsync();

        var dialog = OpenDialog();

        Assert.Same(document, dialog.Document);
        Assert.Equal(_output, dialog.OutputFolder);
        Assert.Equal(WicFormats.JpegId, dialog.Settings.FormatId); // この時点では設定の既定（JPEG）のまま
        Assert.Equal("a_20261117_103000.jpg", dialog.FileName);
    }

    [Fact]
    public async Task Save_RecordsOutputFolder_AndShowsM04()
    {
        // 追加: 出力先が設定に保存される
        await OpenAsync();
        var dialog = OpenDialog();
        _dialogs.FolderToSelect = _output;
        dialog.ChangeFolderCommand.Execute(null);

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal(_output, _vm.Settings.OutputFolder);
        var reloaded = new SettingsService(_formats, _settingsPath).Load().Settings;
        Assert.Equal(_output, reloaded.OutputFolder);
        Assert.Equal(WicFormats.JpegId, reloaded.Export.FormatId);
        Assert.Equal(ExportDialogViewModel.SavedMessage(dialog.Result!), _vm.StatusMessage);
        Assert.StartsWith("保存しました: a_20261117_103000.jpg（", _vm.StatusMessage);
        Assert.True(File.Exists(Path.Combine(_output, "a_20261117_103000.jpg")));
    }

    [Fact]
    public async Task Cancel_ChangesNothing()
    {
        // 追加: キャンセルでは何も変わらない（出力先を変えてから閉じても、設定に記録しない）
        var original = _temp.Combine("original");
        Directory.CreateDirectory(original);
        _vm.Settings.OutputFolder = original;
        _vm.SaveSettingsQuietly();
        var before = File.ReadAllText(_settingsPath);
        var document = await OpenAsync();
        await EditAsync(document);
        var status = _vm.StatusMessage;

        var dialog = OpenDialog();
        _dialogs.FolderToSelect = _output;
        dialog.ChangeFolderCommand.Execute(null); // 変えて、保存せずに閉じた

        Assert.Equal(original, _vm.Settings.OutputFolder);
        Assert.Equal(before, File.ReadAllText(_settingsPath));
        Assert.Equal(status, _vm.StatusMessage);
        Assert.True(document.HasUnexportedEdits);
        Assert.Empty(Directory.GetFiles(_output));
        Assert.Empty(Directory.GetFiles(original));
    }

    [Fact]
    public async Task HasUnexportedEdits_FollowsSave()
    {
        // V32: 開いた直後 → 編集 → 保存 → 元に戻す → やり直し: false → true → false → true → false
        _vm.Settings.OutputFolder = _output;
        var document = await OpenAsync();
        Assert.False(document.HasUnexportedEdits);

        await EditAsync(document);
        Assert.True(document.HasUnexportedEdits);
        Assert.Equal("a.fake *", document.Title);

        await OpenDialog().SaveCommand.ExecuteAsync(null);
        Assert.False(document.HasUnexportedEdits);
        Assert.Equal("a.fake", document.Title);

        await document.UndoAsync();
        Assert.True(document.HasUnexportedEdits); // 保存した時点と違う状態

        await document.RedoAsync();
        Assert.False(document.HasUnexportedEdits); // 保存した時点に戻った
    }

    [Fact]
    public async Task SaveAfterClosing_DoesNotAskOnExit()
    {
        _vm.Settings.OutputFolder = _output;
        var document = await OpenAsync();
        await EditAsync(document);

        await OpenDialog().SaveCommand.ExecuteAsync(null);

        Assert.True(_vm.ConfirmExit()); // 保存した後は、終了の確認（M17）が出ない
        Assert.Empty(_dialogs.Confirmations);
    }
}
