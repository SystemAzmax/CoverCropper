using System.IO;
using WebDeza.Core;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Services;
using WebDeza.ViewModels;

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
