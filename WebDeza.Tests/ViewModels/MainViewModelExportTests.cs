using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Services;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>保存のダイアログを開き、保存した後（設計書 F-08・F-10、V23・V32。実装計画書 10-2・10-3）</summary>
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
    public async Task FormatAndOptions_AreRecordedOnSave_AndUsedNextTime()
    {
        // T13: 各形式とオプションが、次に開いたとき・再起動した後も保たれる
        _vm.Settings.OutputFolder = _output;
        await OpenAsync();
        var dialog = OpenDialog();
        ((NumericParameterViewModel)dialog.Options[0]).SliderValue = 60; // JPEG の品質
        dialog.SelectedFormat = dialog.Formats.Single(f => f.Id == WicFormats.PngId);
        ((BooleanParameterViewModel)dialog.Options[0]).IsChecked = true;

        await dialog.SaveCommand.ExecuteAsync(null);

        var next = OpenDialog(); // 次に開いたとき
        Assert.Equal(WicFormats.PngId, next.SelectedFormat!.Id);
        Assert.Equal(true, next.Options[0].Value);
        next.SelectedFormat = next.Formats.Single(f => f.Id == WicFormats.JpegId);
        Assert.Equal(60L, next.Options[0].Value);

        var reloaded = new SettingsService(_formats, _settingsPath).Load().Settings.Export; // 再起動した後
        Assert.Equal(WicFormats.PngId, reloaded.FormatId);
        Assert.Equal(60, reloaded.FormatOptions[WicFormats.JpegId]!.Get<int>(WicFormats.QualityKey));
        Assert.True(reloaded.FormatOptions[WicFormats.PngId]!.Get<bool>(WicFormats.InterlaceKey));
    }

    [Fact]
    public async Task FormatAndOptions_CancelChangesNothing()
    {
        // V23: キャンセル → 設定が変わらない
        _vm.Settings.OutputFolder = _output;
        _vm.SaveSettingsQuietly();
        var before = File.ReadAllText(_settingsPath);
        await OpenAsync();
        var dialog = OpenDialog();
        ((NumericParameterViewModel)dialog.Options[0]).SliderValue = 30;
        dialog.SelectedFormat = dialog.Formats.Single(f => f.Id == WicFormats.TiffId);
        // 保存せずに閉じた

        Assert.Equal(WicFormats.JpegId, _vm.Settings.Export.FormatId);
        // 設定の JPEG の品質は既定値（85）のまま（読み込むときの検証で、形式ごとの既定値が補われている）
        Assert.Equal(85, _vm.Settings.Export.ToExportSettings().GetOptions(WicFormats.JpegId).Get<int>(WicFormats.QualityKey));
        Assert.Equal(before, File.ReadAllText(_settingsPath));

        var next = OpenDialog();
        Assert.Equal(WicFormats.JpegId, next.SelectedFormat!.Id);
        Assert.Equal(85L, next.Options[0].Value);
    }

    [Fact]
    public async Task Background_IsRecordedOnSave_AndNotOnCancel()
    {
        // T13: 背景色も、次に開いたとき・再起動した後も保たれる。キャンセルでは変わらない
        _vm.Settings.OutputFolder = _output;
        await OpenAsync();
        var cancelled = OpenDialog();
        cancelled.Background.Text = "#123456"; // 保存せずに閉じた
        Assert.Equal("#FFFFFF", _vm.Settings.Export.Background);

        var dialog = OpenDialog();
        Assert.Equal("#FFFFFF", dialog.Background.Text);
        dialog.Background.Text = "#808080";
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal("#808080", _vm.Settings.Export.Background);
        Assert.Equal("#808080", OpenDialog().Background.Text);
        Assert.Equal("#808080", new SettingsService(_formats, _settingsPath).Load().Settings.Export.Background);
    }

    [Fact]
    public async Task SizeLimit_IsRecordedOnSave_AndRememberedInQualityMode()
    {
        _vm.Settings.OutputFolder = _output;
        await OpenAsync();
        var cancelled = OpenDialog();
        Assert.Equal("300", cancelled.SizeLimitText); // 既定は 300 KB
        cancelled.UseSizeLimit = true;                 // 変えて、保存せずに閉じた
        cancelled.SizeLimitText = "120";
        cancelled.OnClosed();
        Assert.False(_vm.Settings.Export.UseSizeLimit);
        Assert.Equal(300, _vm.Settings.Export.SizeLimitKB);

        var dialog = OpenDialog();
        dialog.UseSizeLimit = true;
        dialog.SizeLimitText = "150";
        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.True(_vm.Settings.Export.UseSizeLimit);
        Assert.Equal(150, _vm.Settings.Export.SizeLimitKB);

        var next = OpenDialog(); // 次は上限で開く
        Assert.True(next.UseSizeLimit);
        Assert.Equal("150", next.SizeLimitText);
        next.UseQuality = true;
        next.SizeLimitText = "80";
        await next.SaveCommand.ExecuteAsync(null);

        // 品質を指定にしても、上限の値は覚えておく（再起動した後も）
        var reloaded = new SettingsService(_formats, _settingsPath).Load().Settings.Export;
        Assert.False(reloaded.UseSizeLimit);
        Assert.Equal(80, reloaded.SizeLimitKB);
        Assert.Equal("80", OpenDialog().SizeLimitText);
    }

    [Fact]
    public async Task DialogSize_IsRemembered_EvenWhenCancelled()
    {
        // ダイアログの大きさは覚えておく（設計書 F-08）。ウィンドウの見た目の状態なので、［キャンセル］で閉じても覚える
        await OpenAsync();
        var first = OpenDialog();
        Assert.Null(first.DialogWidth); // まだ無い（既定の大きさで開く）
        var before = File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath) : null;
        // FakeDialogs はすぐ戻る（大きさを変えずに閉じた）→ 設定ファイルは書かない
        Assert.Equal(before, File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath) : null);

        // 大きさを変えて閉じたことにする（ダイアログが閉じるときに書き戻す値）
        _dialogs.OnExportDialogShown = d => { d.DialogWidth = 1234; d.DialogHeight = 777; };
        OpenDialog();

        Assert.Equal((1234.0, 777.0), (_vm.Settings.ExportDialogWidth, _vm.Settings.ExportDialogHeight));
        var reloaded = new SettingsService(_formats, _settingsPath).Load().Settings;
        Assert.Equal((1234.0, 777.0), (reloaded.ExportDialogWidth, reloaded.ExportDialogHeight));

        _dialogs.OnExportDialogShown = null;
        var next = OpenDialog(); // 次は覚えた大きさで開く
        Assert.Equal((1234.0, 777.0), (next.DialogWidth, next.DialogHeight));
    }

    [Fact]
    public async Task CompareMode_IsRemembered_EvenWhenCancelled()
    {
        // 追加: 比較のしかたの切り替えと保存（既定は並べて比較）
        await OpenAsync();
        Assert.True(OpenDialog().IsSideBySide);

        _dialogs.OnExportDialogShown = d => d.IsResultOnly = true; // 「結果のみ」にして、保存せずに閉じた
        OpenDialog();

        Assert.False(_vm.Settings.ExportPreviewSideBySide);
        Assert.False(new SettingsService(_formats, _settingsPath).Load().Settings.ExportPreviewSideBySide);
        _dialogs.OnExportDialogShown = null;
        Assert.True(OpenDialog().IsResultOnly); // 次は結果のみで開く

        var before = File.ReadAllText(_settingsPath);
        OpenDialog(); // 変えずに閉じた → 書かない
        Assert.Equal(before, File.ReadAllText(_settingsPath));
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
