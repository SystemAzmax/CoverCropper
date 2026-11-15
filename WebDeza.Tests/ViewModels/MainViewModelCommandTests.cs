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
    }

    // ---------------------------------------------------------------- 道具（8-4）

    private MainViewModel CreateWithTools(out ToolRegistry tools)
    {
        tools = new ToolRegistry();
        tools.Register(new WebDeza.Tests.Views.FakeToolFactory("select", "選択", System.Windows.Input.Key.M));
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
