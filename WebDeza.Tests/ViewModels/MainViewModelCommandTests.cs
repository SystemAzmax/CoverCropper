using System.IO;
using WebDeza.Core;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Services;
using WebDeza.ViewModels;
using WebDeza.Views.Canvas;

namespace WebDeza.Tests.ViewModels;

/// <summary>
/// メニューとツールバーのコマンド（設計書 4.2・4.3、V42。実装計画書 7-1 から）。
/// コマンドが出来るステップで、そのコマンドの有効・無効の確認をここに足していく。
/// </summary>
public sealed class MainViewModelCommandTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeFormat _fake = new("fake", ".fake");
    private readonly FormatRegistry _formats = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly MainViewModel _vm;

    public MainViewModelCommandTests()
    {
        _formats.AddWicFormats();
        _formats.Register(_fake);
        _vm = new MainViewModel(_formats, new SettingsService(_formats, _temp.Combine("settings.json")),
            _dialogs, new AppLog(_temp.Combine("logs", "app.log")));
    }

    public void Dispose() => _temp.Dispose();

    private string CreateFile(string name)
    {
        var path = _temp.Combine(name);
        File.WriteAllBytes(path, new byte[16]);
        return path;
    }

    // ---------------------------------------------------------------- V42: 画像が無いとき

    [Fact]
    public void NoImage_OpenAndExitAreEnabled()
    {
        Assert.False(_vm.HasImage);
        Assert.True(_vm.OpenCommand.CanExecute(null));
        Assert.True(_vm.ExitCommand.CanExecute(null));
        Assert.False(_vm.CloseCommand.CanExecute(null));
        Assert.False(_vm.CloseAllCommand.CanExecute(null));
        Assert.False(_vm.CascadeWindowsCommand.CanExecute(null));
        Assert.False(_vm.TileWindowsCommand.CanExecute(null));
        Assert.False(_vm.ZoomInCommand.CanExecute(null));
        Assert.False(_vm.ZoomOutCommand.CanExecute(null));
        Assert.False(_vm.ZoomToFitCommand.CanExecute(null));
        Assert.False(_vm.ZoomToActualSizeCommand.CanExecute(null));
        Assert.False(_vm.SelectAllCommand.CanExecute(null));
        Assert.False(_vm.DeselectCommand.CanExecute(null));
        Assert.False(_vm.CropCommand.CanExecute(null));
        Assert.False(_vm.UndoCommand.CanExecute(null));
        Assert.False(_vm.RedoCommand.CanExecute(null));
        Assert.Equal("元に戻す(_U)", _vm.UndoHeader);
        Assert.Equal("やり直し(_R)", _vm.RedoHeader);
    }

    // ---------------------------------------------------------------- 元に戻す・やり直し（V29・V42。9-2）

    [Fact]
    public async Task UndoRedo_DisabledWithoutHistory()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));

        Assert.False(_vm.UndoCommand.CanExecute(null)); // V42: 履歴が無いときは無効
        Assert.False(_vm.RedoCommand.CanExecute(null));
        Assert.Equal("元に戻す (Ctrl+Z)", _vm.UndoToolTip);
        Assert.Equal("やり直し (Ctrl+Y)", _vm.RedoToolTip);
    }

    [Fact]
    public async Task UndoRedo_AfterCrop_ShowsOperationName()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var document = _vm.ActiveDocument!;
        document.Selection = new WebDeza.Imaging.PixelRect(1, 2, 10, 8);
        var undoChanged = 0;
        var properties = new List<string?>();
        _vm.UndoCommand.CanExecuteChanged += (_, _) => undoChanged++;
        _vm.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        await _vm.CropCommand.ExecuteAsync(null);

        Assert.True(_vm.UndoCommand.CanExecute(null));
        Assert.False(_vm.RedoCommand.CanExecute(null));
        Assert.True(undoChanged > 0);
        Assert.Contains(nameof(MainViewModel.UndoHeader), properties);
        Assert.Equal("元に戻す: 切り抜き(_U)", _vm.UndoHeader);
        Assert.Equal("元に戻す: 切り抜き (Ctrl+Z)", _vm.UndoToolTip);
        Assert.Equal("やり直し(_R)", _vm.RedoHeader);

        await _vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal((30, 20), (document.ImageWidth, document.ImageHeight));
        Assert.False(document.HasUnexportedEdits);
        Assert.False(_vm.UndoCommand.CanExecute(null));
        Assert.True(_vm.RedoCommand.CanExecute(null));
        Assert.Equal("元に戻す(_U)", _vm.UndoHeader);
        Assert.Equal("やり直し: 切り抜き(_R)", _vm.RedoHeader);
        Assert.Equal("やり直し: 切り抜き (Ctrl+Y)", _vm.RedoToolTip);
        Assert.Equal("画像: 30×20", _vm.StatusText);

        await _vm.RedoCommand.ExecuteAsync(null);

        Assert.Equal((10, 8), (document.ImageWidth, document.ImageHeight));
        Assert.True(document.HasUnexportedEdits);
        Assert.Equal("元に戻す: 切り抜き(_U)", _vm.UndoHeader);
        Assert.Equal("やり直し(_R)", _vm.RedoHeader);
    }

    [Fact]
    public async Task Undo_UsesHistoryOfActiveDocument()
    {
        // V29: 画像 A を編集 → B をアクティブに → A に戻って元に戻す → A の編集が 1 段戻る（B は変わらない）
        await _vm.OpenPathsAsync([CreateFile("a.fake"), CreateFile("b.fake")]);
        var a = _vm.Documents[0];
        var b = _vm.Documents[1];
        _vm.ActiveDocument = a;
        a.Selection = new WebDeza.Imaging.PixelRect(0, 0, 10, 10);
        await _vm.CropCommand.ExecuteAsync(null);
        b.Selection = new WebDeza.Imaging.PixelRect(0, 0, 20, 10);
        _vm.ActiveDocument = b;
        await _vm.CropCommand.ExecuteAsync(null);
        await _vm.UndoCommand.ExecuteAsync(null); // B の履歴を戻す（A は変わらない）
        Assert.Equal((30, 20), (b.ImageWidth, b.ImageHeight));
        Assert.Equal((10, 10), (a.ImageWidth, a.ImageHeight));

        Assert.False(_vm.UndoCommand.CanExecute(null)); // B にはもう戻す編集が無い
        Assert.Equal("元に戻す(_U)", _vm.UndoHeader);

        _vm.ActiveDocument = a;
        Assert.True(_vm.UndoCommand.CanExecute(null));
        Assert.Equal("元に戻す: 切り抜き(_U)", _vm.UndoHeader);
        await _vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal((30, 20), (a.ImageWidth, a.ImageHeight));
        Assert.False(a.CanUndo);
        Assert.True(b.CanRedo); // B の履歴はそのまま
        Assert.Equal((30, 20), (b.ImageWidth, b.ImageHeight));
    }

    [Fact]
    public async Task UndoRedo_DisabledWhileBusy()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var document = _vm.ActiveDocument!;
        document.Selection = new WebDeza.Imaging.PixelRect(0, 0, 10, 10);
        await _vm.CropCommand.ExecuteAsync(null);
        await _vm.UndoCommand.ExecuteAsync(null);
        var gate = new ManualResetEventSlim();

        var editing = document.ApplyEditAsync("編集", (image, _) => { gate.Wait(TimeSpan.FromSeconds(10)); return image; },
            changedRegion: null, newSelection: null, resetView: false);
        Assert.False(_vm.UndoCommand.CanExecute(null));
        Assert.False(_vm.RedoCommand.CanExecute(null));
        gate.Set();
        await editing;

        Assert.True(_vm.UndoCommand.CanExecute(null));
        Assert.Equal("元に戻す: 編集(_U)", _vm.UndoHeader);
        Assert.False(_vm.RedoCommand.CanExecute(null)); // 新しく積んだので、やり直す先は消える
    }

    [Theory]
    [InlineData("元に戻す", null, "U", "元に戻す(_U)")]
    [InlineData("元に戻す", "ぼかし", "U", "元に戻す: ぼかし(_U)")]
    [InlineData("やり直し", "a_b", "R", "やり直し: a__b(_R)")] // 「_」はアクセスキーの印にしない
    public void HistoryHeader_Format(string action, string? name, string key, string expected)
        => Assert.Equal(expected, MainViewModel.HistoryHeader(action, name, key));

    // ---------------------------------------------------------------- 切り抜き（V33。9-1）

    /// <summary>画素ごとに違う値を持つ画像（切り抜いた位置を確かめられる）</summary>
    private static WebDeza.Imaging.ImageBuffer Pattern(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7 % 251);
        return WebDeza.Imaging.ImageBuffer.Create(width, height, pixels);
    }

    [Fact]
    public async Task Crop_CutsOutSelection_ClearsSelection_AndResetsView()
    {
        var source = Pattern(600, 400);
        _fake.Image = source;
        await _vm.LoadAsync(CreateFile("a.fake"));
        var document = _vm.ActiveDocument!;
        document.Selection = new WebDeza.Imaging.PixelRect(10, 20, 300, 200);

        Assert.True(_vm.CropCommand.CanExecute(null));
        await _vm.CropCommand.ExecuteAsync(null);

        var image = document.Image;
        Assert.Equal((300, 200), (image.Width, image.Height));
        for (int y = 0; y < 200; y++)
        {
            Assert.True(source.Pixels.AsSpan(((20 + y) * 600 + 10) * 4, 300 * 4)
                .SequenceEqual(image.Pixels.AsSpan(y * 300 * 4, 300 * 4)), $"y = {y}");
        }
        Assert.Null(document.Selection);
        Assert.Equal(1, document.ResetViewRequest);
        Assert.Equal((300, 200), (document.Display.ImageWidth, document.Display.ImageHeight));
        Assert.Equal(MainViewModel.CropEditName, document.History.UndoName);
        Assert.True(document.HasUnexportedEdits);
        Assert.Equal("a.fake *", document.Title);
        Assert.Equal("画像: 300×200", _vm.StatusText); // 前の操作の知らせ（M09）は消す
        Assert.False(_vm.CropCommand.CanExecute(null)); // 選択範囲が無くなったので無効
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task Crop_EnabledOnlyWithSelection()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var document = _vm.ActiveDocument!;
        var changed = 0;
        _vm.CropCommand.CanExecuteChanged += (_, _) => changed++;

        Assert.False(_vm.CropCommand.CanExecute(null)); // V33・V42: 選択範囲が無いときは無効
        document.Selection = new WebDeza.Imaging.PixelRect(1, 1, 5, 5);
        Assert.True(_vm.CropCommand.CanExecute(null));
        Assert.True(changed > 0); // メニューに知らせる

        document.Selection = null;
        Assert.False(_vm.CropCommand.CanExecute(null));
        await _vm.CropCommand.ExecuteAsync(null); // 無効のときに呼ばれても何もしない
        Assert.False(document.History.CanUndo);
    }

    [Fact]
    public async Task Crop_FollowsActiveDocument()
    {
        await _vm.OpenPathsAsync([CreateFile("a.fake"), CreateFile("b.fake")]);
        _vm.Documents[0].Selection = new WebDeza.Imaging.PixelRect(1, 1, 5, 5);

        _vm.ActiveDocument = _vm.Documents[1];
        Assert.False(_vm.CropCommand.CanExecute(null));
        _vm.ActiveDocument = _vm.Documents[0];
        Assert.True(_vm.CropCommand.CanExecute(null));

        await _vm.CropCommand.ExecuteAsync(null);
        Assert.Equal((5, 5), (_vm.Documents[0].ImageWidth, _vm.Documents[0].ImageHeight));
        Assert.Equal((30, 20), (_vm.Documents[1].ImageWidth, _vm.Documents[1].ImageHeight)); // ほかの画像は変わらない
        Assert.False(_vm.Documents[1].HasUnexportedEdits);
    }

    [Fact]
    public async Task Editing_MakesAppBusy_UntilDone()
    {
        // 編集の確定中は、メニュー・ツールバー・編集パネルを無効にし、待機カーソルにする（設計書 4.2）
        await _vm.OpenPathsAsync([CreateFile("a.fake"), CreateFile("b.fake")]);
        var document = _vm.ActiveDocument!;
        document.Selection = new WebDeza.Imaging.PixelRect(1, 1, 5, 5);
        var gate = new ManualResetEventSlim();

        var editing = document.ApplyEditAsync("編集", (image, _) => { gate.Wait(TimeSpan.FromSeconds(10)); return image; },
            changedRegion: null, newSelection: document.Selection, resetView: false);

        Assert.True(_vm.IsBusy);
        Assert.False(_vm.IsEditPanelEnabled);
        Assert.False(_vm.CropCommand.CanExecute(null));
        Assert.False(_vm.OpenCommand.CanExecute(null));
        Assert.False(_vm.CloseCommand.CanExecute(null));
        Assert.False(_vm.CloseAllCommand.CanExecute(null));
        Assert.False(_vm.ExitCommand.CanExecute(null));

        // 確定中にほかの画像に切り替わっても、確定が終わるまでは確定中のまま
        _vm.ActiveDocument = _vm.Documents.First(d => d != document);
        Assert.True(_vm.IsBusy);

        gate.Set();
        await editing;

        Assert.False(_vm.IsBusy);
        Assert.True(_vm.OpenCommand.CanExecute(null));
        Assert.Equal(1, document.History.UndoCount);
    }

    [Fact]
    public async Task EditFailure_ShowsM28_AfterBusyEnds()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var document = _vm.ActiveDocument!;
        bool busyWhenShown = true;
        _dialogs.OnShowError = _ => busyWhenShown = _vm.IsBusy;

        await document.ApplyEditAsync("編集", (_, _) => throw new InvalidOperationException("x"),
            changedRegion: null, newSelection: null, resetView: true);

        Assert.Equal([Messages.M28_EditFailed("x")], _dialogs.Errors);
        Assert.False(busyWhenShown); // 待機カーソルのままメッセージを出さない
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task ClosedDocument_NoLongerMakesAppBusy()
    {
        await _vm.OpenPathsAsync([CreateFile("a.fake"), CreateFile("b.fake")]);
        var closed = _vm.Documents[0];
        _vm.CloseCommand.Execute(closed);
        var cleared = _vm.Documents[0];
        _vm.CloseAllCommand.Execute(null);

        // 閉じた画像を（参照が残っていて）編集しても、アプリは確定中にならない
        var busy = new List<bool>();
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.IsBusy)) busy.Add(_vm.IsBusy); };
        await closed.ApplyEditAsync("編集", (image, _) => image, null, null, false);
        await cleared.ApplyEditAsync("編集", (image, _) => image, null, null, false);

        Assert.Empty(busy);
    }

    // ---------------------------------------------------------------- 選択範囲（8-5）

    [Fact]
    public async Task SelectAll_SelectsWholeImage()
    {
        _fake.Image = WebDeza.Imaging.ImageBuffer.Create(120, 80, new byte[120 * 80 * 4]);
        await _vm.LoadAsync(CreateFile("a.fake"));

        Assert.True(_vm.SelectAllCommand.CanExecute(null));
        _vm.SelectAllCommand.Execute(null);

        Assert.Equal(new WebDeza.Imaging.PixelRect(0, 0, 120, 80), _vm.ActiveDocument!.Selection);
    }

    [Fact]
    public async Task SelectAll_WithAspect_LargestRectInCenter()
    {
        // 比率が決まっていれば、その比率でいちばん大きい矩形を中央に置く（CropFrameCalculator.Initial）
        _fake.Image = WebDeza.Imaging.ImageBuffer.Create(120, 80, new byte[120 * 80 * 4]);
        _vm.Settings.SelectionAspect = WebDeza.Models.SelectionAspectData.Fixed(1, 1);
        await _vm.LoadAsync(CreateFile("a.fake"));

        _vm.SelectAllCommand.Execute(null);

        Assert.Equal(new WebDeza.Imaging.PixelRect(20, 0, 80, 80), _vm.ActiveDocument!.Selection);
    }

    [Fact]
    public async Task Deselect_EnabledOnlyWithSelection()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var document = _vm.ActiveDocument!;
        var changed = 0;
        _vm.DeselectCommand.CanExecuteChanged += (_, _) => changed++;

        Assert.False(_vm.DeselectCommand.CanExecute(null)); // V42: 選択範囲が無いときは無効
        document.Selection = new WebDeza.Imaging.PixelRect(1, 1, 5, 5);
        Assert.True(_vm.DeselectCommand.CanExecute(null));
        Assert.True(changed > 0); // メニューに知らせる

        _vm.DeselectCommand.Execute(null);

        Assert.Null(document.Selection);
        Assert.False(_vm.DeselectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Deselect_FollowsActiveDocument()
    {
        await _vm.OpenPathsAsync([CreateFile("a.fake"), CreateFile("b.fake")]);
        _vm.Documents[0].Selection = new WebDeza.Imaging.PixelRect(1, 1, 5, 5);

        _vm.ActiveDocument = _vm.Documents[1];
        Assert.False(_vm.DeselectCommand.CanExecute(null));
        _vm.ActiveDocument = _vm.Documents[0];
        Assert.True(_vm.DeselectCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 編集パネル（8-7）

    [Fact]
    public async Task EditPanel_FollowsActiveDocument_AndDisabledWithoutImageOrWhileBusy()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(_vm.IsEditPanelEnabled); // 画像が無い（設計書 4.2）
        Assert.Null(_vm.SelectionPanel.Document);

        await _vm.OpenPathsAsync([CreateFile("a.fake"), CreateFile("b.fake")]);
        Assert.True(_vm.IsEditPanelEnabled);
        Assert.Contains(nameof(MainViewModel.IsEditPanelEnabled), changed);
        Assert.Same(_vm.ActiveDocument, _vm.SelectionPanel.Document);

        _vm.Documents[0].Selection = new WebDeza.Imaging.PixelRect(1, 2, 3, 4);
        _vm.ActiveDocument = _vm.Documents[0];
        Assert.Equal("3", _vm.SelectionPanel.WidthText);

        _vm.IsBusy = true; // 読み込み中・編集の確定中
        Assert.False(_vm.IsEditPanelEnabled);
        _vm.IsBusy = false;

        _vm.CloseAllCommand.Execute(null);
        Assert.False(_vm.IsEditPanelEnabled);
        Assert.Null(_vm.SelectionPanel.Document);
    }

    [Fact]
    public async Task UseSelectTool_SwitchesToSelect()
    {
        var vm = CreateWithTools(out var tools);
        await vm.LoadAsync(CreateFile("a.fake"));
        vm.SelectToolByShortcut(System.Windows.Input.Key.H);

        Assert.True(vm.UseSelectToolCommand.CanExecute(null));
        vm.UseSelectToolCommand.Execute(null);

        Assert.Equal(SelectToolFactory.ToolId, vm.CurrentTool!.Id);
    }

    [Fact]
    public void UseSelectTool_NoImage_Disabled()
    {
        var vm = CreateWithTools(out _);

        Assert.False(vm.UseSelectToolCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 道具（8-4）

    private MainViewModel CreateWithTools(out ToolRegistry tools)
    {
        tools = new ToolRegistry();
        tools.Register(new SelectToolFactory());
        tools.Register(new HandToolFactory());
        return new MainViewModel(_formats, new SettingsService(_formats, _temp.Combine("settings2.json")),
            _dialogs, new AppLog(_temp.Combine("logs", "app2.log")), tools);
    }

    [Fact]
    public void Tools_ButtonsFollowRegistrationAndFirstIsDefault()
    {
        var vm = CreateWithTools(out var tools);

        Assert.Equal(new[] { "M", "H" }, vm.ToolItems.Select(t => t.ShortcutText));
        Assert.Equal(new[] { "選択 (M)", "手のひら (H)" }, vm.ToolItems.Select(t => t.ToolTip));
        Assert.Same(tools.All[0], vm.CurrentTool);
        Assert.Equal(new[] { true, false }, vm.ToolItems.Select(t => t.IsSelected));
    }

    [Fact]
    public void Tools_NoImage_CannotSwitch()
    {
        var vm = CreateWithTools(out var tools);

        Assert.False(vm.SelectToolCommand.CanExecute(vm.ToolItems[1]));
        Assert.False(vm.SelectToolByShortcut(System.Windows.Input.Key.H));
        Assert.Same(tools.All[0], vm.CurrentTool);
    }

    [Fact]
    public async Task Tools_WithImage_SwitchByButtonAndKey()
    {
        var vm = CreateWithTools(out var tools);
        await vm.LoadAsync(CreateFile("a.fake"));

        Assert.True(vm.SelectToolCommand.CanExecute(vm.ToolItems[1]));
        vm.SelectToolCommand.Execute(vm.ToolItems[1]);
        Assert.Same(tools.All[1], vm.CurrentTool);
        Assert.Equal(new[] { false, true }, vm.ToolItems.Select(t => t.IsSelected));

        Assert.True(vm.SelectToolByShortcut(System.Windows.Input.Key.M));
        Assert.Same(tools.All[0], vm.CurrentTool);
        Assert.False(vm.SelectToolByShortcut(System.Windows.Input.Key.Q)); // 登録の無いキー
        Assert.Same(tools.All[0], vm.CurrentTool);
    }

    [Fact]
    public async Task Tools_WhileLoading_CannotSwitch()
    {
        var vm = CreateWithTools(out var tools);
        await vm.LoadAsync(CreateFile("a.fake"));
        var gate = new ManualResetEventSlim();
        _fake.OnDecode = () => gate.Wait(TimeSpan.FromSeconds(10));

        var loading = vm.LoadAsync(CreateFile("b.fake"));
        Assert.False(vm.SelectToolByShortcut(System.Windows.Input.Key.H));
        Assert.False(vm.SelectToolCommand.CanExecute(vm.ToolItems[1]));
        gate.Set();
        await loading;

        Assert.True(vm.SelectToolByShortcut(System.Windows.Input.Key.H));
        Assert.Same(tools.All[1], vm.CurrentTool);
    }

    [Fact]
    public void Tools_NoneRegistered_NoCurrentTool()
    {
        Assert.Empty(_vm.ToolItems);
        Assert.Null(_vm.CurrentTool);
    }

    // ---------------------------------------------------------------- ズーム（8-2）

    [Fact]
    public async Task Zoom_WithImage_RequestsActionForActiveCanvas()
    {
        var requests = new List<ZoomAction>();
        _vm.ZoomRequested += (_, action) => requests.Add(action);
        await _vm.LoadAsync(CreateFile("a.fake"));

        Assert.True(_vm.ZoomInCommand.CanExecute(null));
        _vm.ZoomInCommand.Execute(null);
        _vm.ZoomOutCommand.Execute(null);
        _vm.ZoomToFitCommand.Execute(null);
        _vm.ZoomToActualSizeCommand.Execute(null);

        Assert.Equal(new[] { ZoomAction.In, ZoomAction.Out, ZoomAction.Fit, ZoomAction.ActualSize }, requests);
    }

    [Fact]
    public async Task Zoom_DisabledWhileLoadingAndAfterClosing()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var gate = new ManualResetEventSlim();
        _fake.OnDecode = () => gate.Wait(TimeSpan.FromSeconds(10));

        var loading = _vm.LoadAsync(CreateFile("b.fake"));
        Assert.False(_vm.ZoomInCommand.CanExecute(null));
        gate.Set();
        await loading;
        Assert.True(_vm.ZoomInCommand.CanExecute(null));

        _vm.CloseAllCommand.Execute(null);
        Assert.False(_vm.ZoomToFitCommand.CanExecute(null));
    }

    [Fact]
    public async Task StatusText_ShowsZoomPercentOfActiveDocument()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var document = _vm.ActiveDocument!;
        var changed = 0;
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.StatusText)) changed++; };

        document.ZoomPercent = 37.4; // キャンバスが書く
        document.Selection = new WebDeza.Imaging.PixelRect(1, 2, 3, 4);

        Assert.Equal("画像: 30×20 ／ 選択範囲: (1, 2) 3×4 ／ 37% ／ " + Messages.M09_Opened(1, 0), _vm.StatusText);
        Assert.True(changed >= 2);
    }

    [Theory]
    [InlineData(37.4, "37%")]
    [InlineData(156.25, "156%")]
    [InlineData(100, "100%")]
    [InlineData(10, "10%")]
    [InlineData(6.25, "6.3%")]
    [InlineData(3.0, "3%")]
    [InlineData(3200, "3200%")]
    public void FormatZoomPercent_IntegerFrom10Percent(double percent, string expected)
    {
        Assert.Equal(expected, MainViewModel.FormatZoomPercent(percent));
    }

    // ---------------------------------------------------------------- 閉じる（7-2）

    [Fact]
    public async Task Close_ActiveDocument_DocumentsBecomeEmpty()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        Assert.True(_vm.CloseCommand.CanExecute(null));
        var closeChanged = 0;
        _vm.CloseCommand.CanExecuteChanged += (_, _) => closeChanged++;

        _vm.CloseCommand.Execute(null); // メニュー / Ctrl+W は引数なしで、アクティブな画像を閉じる

        Assert.Empty(_vm.Documents);
        Assert.Null(_vm.ActiveDocument);
        Assert.False(_vm.HasImage);
        Assert.Equal("webDeza", _vm.WindowTitle);
        Assert.Equal("", _vm.StatusText);
        Assert.False(_vm.CloseCommand.CanExecute(null));
        Assert.True(closeChanged > 0);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task Close_WithDocument_ClosesThatDocument()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var document = _vm.ActiveDocument!;

        Assert.True(_vm.CloseCommand.CanExecute(document));
        _vm.CloseCommand.Execute(document); // 子ウィンドウの［閉じる］は、その画像を渡す

        Assert.Empty(_vm.Documents);
        Assert.Null(_vm.ActiveDocument);
    }

    [Fact]
    public async Task Close_DoesNotDeleteFile()
    {
        var path = CreateFile("a.fake");
        await _vm.LoadAsync(path);

        _vm.CloseCommand.Execute(null);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Close_ReleasesDocument()
    {
        // 閉じた画像（画像・表示用画像・履歴）を、MainViewModel が持ち続けない（設計書 5.10-11）
        var weak = await LoadAndGetWeakReference();

        _vm.CloseCommand.Execute(null);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.IsAlive);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private async Task<WeakReference> LoadAndGetWeakReference()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        return new WeakReference(_vm.ActiveDocument);
    }

    [Fact]
    public async Task WhileLoading_CloseIsDisabled()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var gate = new ManualResetEventSlim();
        _fake.OnDecode = () => gate.Wait(TimeSpan.FromSeconds(10));

        var loading = _vm.LoadAsync(CreateFile("b.fake"));

        Assert.False(_vm.CloseCommand.CanExecute(null));
        _vm.CloseCommand.Execute(null); // 読み込み中に頼まれても閉じない
        Assert.Single(_vm.Documents);
        gate.Set();
        await loading;

        Assert.True(_vm.CloseCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 終了

    [Fact]
    public void Exit_RaisesExitRequested()
    {
        var raised = 0;
        _vm.ExitRequested += (sender, _) =>
        {
            Assert.Same(_vm, sender);
            raised++;
        };

        _vm.ExitCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Exit_RemainsEnabledAfterOpeningImage()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));

        Assert.True(_vm.HasImage);
        Assert.True(_vm.OpenCommand.CanExecute(null));
        Assert.True(_vm.ExitCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 読み込み中（設計書 4.2: メニューを無効にする）

    [Fact]
    public async Task WhileLoading_OpenAndExitAreDisabled()
    {
        var gate = new ManualResetEventSlim();
        _fake.OnDecode = () => gate.Wait(TimeSpan.FromSeconds(10));
        var canExecuteChanged = 0;
        _vm.ExitCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;

        var loading = _vm.LoadAsync(CreateFile("a.fake"));

        Assert.True(_vm.IsBusy);
        Assert.False(_vm.OpenCommand.CanExecute(null));
        Assert.False(_vm.ExitCommand.CanExecute(null));
        gate.Set();
        await loading;

        Assert.True(_vm.ExitCommand.CanExecute(null));
        Assert.Equal(2, canExecuteChanged); // 読み込みの始めと終わりに、メニューへ知らせる
    }
}
