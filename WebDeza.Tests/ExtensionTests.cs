using System.IO;
using System.Windows.Input;
using WebDeza.Composition;
using WebDeza.Core;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Services;
using WebDeza.Tests.Views;
using WebDeza.ViewModels;
using WebDeza.Views;
using WebDeza.Views.Canvas;

namespace WebDeza.Tests;

/// <summary>
/// 拡張の確認（設計書 1.3・5.6。実装計画書 11-1）: 標準の登録（AppModules.Register）の後に、テスト用の形式・フィルター・道具を
/// 1 行ずつ足すだけで、画面のコードを変えずに「開く」の種類・保存の形式の一覧とオプションの入力欄・フィルターの一覧（メニュー［フィルター］と
/// 編集パネルの選択欄の元）・道具のボタンの一覧に現れ、使えること
/// </summary>
public sealed class ExtensionTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FormatRegistry _formats = new();
    private readonly FilterRegistry _filters = new();
    private readonly ToolRegistry _tools = new();

    // 足すもの（AppModules に 1 行ずつ足すのと同じ）
    private readonly FakeFormat _readFormat = new("fakeimg", ".fakeimg") { DisplayName = "偽の画像" };
    private readonly FakeEncoderFormat _writeFormat = new("fakeout", alpha: true, ".fakeout");
    private readonly FakeFilter _filter = new("fakefilter");
    private readonly FakeToolFactory _tool = new("faketool", "偽の道具", Key.J);

    private readonly MainViewModel _vm;

    public ExtensionTests()
    {
        AppModules.Register(_formats, _filters, _tools);
        _formats.Register(_readFormat);
        _formats.Register(_writeFormat);
        _filters.Register(_filter);
        _tools.Register(_tool);

        _vm = new MainViewModel(_formats, new SettingsService(_formats, _temp.Combine("settings.json")),
            _dialogs, new AppLog(_temp.Combine("logs", "app.log")), _tools, _filters);
    }

    public void Dispose() => _temp.Dispose();

    private async Task<DocumentViewModel> OpenFakeImageAsync()
    {
        var path = _temp.Combine("photo.fakeimg");
        File.WriteAllBytes(path, new byte[16]);
        await _vm.LoadAsync(path);
        return Assert.Single(_vm.Documents);
    }

    // ---------------------------------------------------------------- 形式

    [Fact]
    public async Task AddedReadFormat_AppearsInOpenDialog_AndOpens()
    {
        await _vm.OpenCommand.ExecuteAsync(null); // 「開く」でやめた

        var filter = Assert.Single(_dialogs.OpenRequests).Filter.Split('|');
        // 「画像ファイル」に拡張子が加わり、標準の形式の後ろ・「すべてのファイル」の前に、足した形式の行が並ぶ
        // （読めない形式（保存だけの形式）は出ない）
        Assert.Equal("画像ファイル", filter[0]);
        Assert.EndsWith(";*.avif;*.fakeimg", filter[1]);
        Assert.Equal(("AVIF (*.avif)", "*.avif"), (filter[^6], filter[^5]));
        Assert.Equal(("偽の画像 (*.fakeimg)", "*.fakeimg"), (filter[^4], filter[^3]));
        Assert.Equal(("すべてのファイル", "*.*"), (filter[^2], filter[^1]));
        Assert.DoesNotContain("fakeout", string.Join("|", filter));

        // 足した形式のファイルを開ける
        var document = await OpenFakeImageAsync();
        Assert.Equal(1, _readFormat.DecodeCount);
        Assert.Equal((30, 20), (document.Image.Width, document.Image.Height));
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task AddedWriteFormat_AppearsInExportDialogWithOptions_AndSaves()
    {
        await OpenFakeImageAsync();
        var output = _temp.Combine("out");
        Directory.CreateDirectory(output);
        _dialogs.FolderToSelect = output;

        _vm.ExportCommand.Execute(null);
        var dialog = Assert.Single(_dialogs.ExportDialogs);

        // 保存の形式の一覧: 標準の 4 形式の後ろに、足した形式（保存できる形式だけ。読むだけの形式は出ない）
        Assert.Equal(new[] { "jpeg", "png", "bmp", "tiff", "fakeout" }, dialog.Formats.Select(f => f.Id));

        // 選ぶと、オプションの記述から入力欄ができ、ファイル名の拡張子が変わる
        dialog.SelectedFormat = _writeFormat;
        var quality = Assert.Single(dialog.Options);
        Assert.Equal(("品質", 85L), (quality.DisplayName, Convert.ToInt64(quality.Value)));
        Assert.EndsWith(".fakeout", dialog.FileName);

        // 足した形式で保存できる（エンコーダーが作ったバイト列がそのまま書かれる）
        dialog.ChangeFolderCommand.Execute(null);
        await dialog.SaveCommand.ExecuteAsync(null);
        var saved = Assert.IsType<ExportResult>(dialog.Result);
        Assert.Equal(".fakeout", Path.GetExtension(saved.Path));
        Assert.Equal("FAKE 30x20 q=85", File.ReadAllText(saved.Path));
        Assert.Empty(_dialogs.Errors);
    }

    // ---------------------------------------------------------------- フィルター

    [Fact]
    public async Task AddedFilter_AppearsInFilterList_AndApplies()
    {
        var document = await OpenFakeImageAsync();

        // メニュー［フィルター］と編集パネルの選択欄は、この一覧から作る（標準のフィルターの後ろに、登録順）
        Assert.Equal(new[] { BlurFilter.FilterId, BasicAdjustFilter.FilterId, "fakefilter" },
            _vm.FilterPanel.Filters.Select(f => f.Id));
        Assert.Equal("テスト用 fakefilter…", MainWindow.FilterMenuHeader(_filter.DisplayName));

        // メニューの項目と同じコマンドで選ぶと、パラメーターの記述から入力欄ができる
        Assert.True(_vm.SelectFilterCommand.CanExecute(_filter));
        _vm.SelectFilterCommand.Execute(_filter);
        Assert.Same(_filter, _vm.FilterPanel.SelectedFilter);
        Assert.True(_vm.FilterPanel.IsExpanded);
        var amount = Assert.IsType<NumericParameterViewModel>(Assert.Single(_vm.FilterPanel.Parameters));
        Assert.Equal("量", amount.DisplayName);

        // 値を変えて［適用］すると、画像にかかり、履歴に足したフィルターの名前で積まれる
        amount.SliderValue = 9;
        await _vm.FilterPanel.PreviewTask;
        await _vm.FilterPanel.ApplyCommand.ExecuteAsync(null);
        await _vm.FilterPanel.PreviewTask;

        Assert.All(document.Image.Pixels, p => Assert.Equal(9, p));
        Assert.Equal(_filter.DisplayName, document.UndoName);
        Assert.Empty(_dialogs.Errors);
    }

    // ---------------------------------------------------------------- 道具

    [Fact]
    public async Task AddedTool_AppearsInToolButtons_AndIsSelectable()
    {
        // 道具のボタンは、この一覧から作る（標準の道具の後ろに、登録順。最初の「選択」が既定のまま）
        Assert.Equal(new[] { SelectToolFactory.ToolId, HandToolFactory.ToolId, "faketool" },
            _vm.ToolItems.Select(t => t.Factory.Id));
        Assert.Equal(SelectToolFactory.ToolId, _vm.CurrentTool?.Id);

        await OpenFakeImageAsync();

        // 足した道具のキーで切り替わる
        Assert.True(_vm.SelectToolByShortcut(Key.J));
        Assert.Same(_tool, _vm.CurrentTool);
        Assert.True(_vm.ToolItems[2].IsSelected);

        // ボタンと同じコマンドでも切り替わる
        _vm.SelectToolCommand.Execute(_vm.ToolItems[0]);
        Assert.Equal(SelectToolFactory.ToolId, _vm.CurrentTool?.Id);
        _vm.SelectToolCommand.Execute(_vm.ToolItems[2]);
        Assert.Same(_tool, _vm.CurrentTool);
    }
}
