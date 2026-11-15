using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Services;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>画像を開く（実装計画書 5-4・5-5、6-5 で載せ替え）。形式とダイアログを差し替えて、ファイルの中身や画面を使わずに確かめる</summary>
public sealed class MainViewModelLoadTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeFormat _fake = new("fake", ".fake");
    private readonly FakeFormat _needsExtension = new("heavy", ".heavy") { Requirement = "Microsoft Store の「重い画像拡張機能」" };
    private readonly FormatRegistry _formats = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly MainViewModel _vm;

    public MainViewModelLoadTests()
    {
        // 実際のアプリと同じく標準の形式を登録し（設定の検証に、書き出せる形式が要る）、偽の形式を足す
        _formats.AddWicFormats();
        _formats.Register(_fake);
        _formats.Register(_needsExtension);
        _vm = CreateViewModel();
    }

    public void Dispose() => _temp.Dispose();

    private string SettingsPath => _temp.Combine("settings.json");

    private string LogPath => _temp.Combine("logs", "app.log");

    private MainViewModel CreateViewModel()
        => new(_formats, new SettingsService(_formats, SettingsPath), _dialogs, new AppLog(LogPath));

    /// <summary>中身のあるファイルを作る（中身は偽の形式が読むので何でもよい）</summary>
    private string CreateFile(string name, int bytes = 1234)
    {
        var path = _temp.Combine(name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    // ---------------------------------------------------------------- 最初の状態

    [Fact]
    public void Initially_HasNoImage()
    {
        Assert.False(_vm.HasImage);
        Assert.Null(_vm.ActiveDocument);
        Assert.Empty(_vm.Documents);
        Assert.Equal("webDeza", _vm.WindowTitle);
        Assert.Equal("", _vm.StatusText);
        Assert.True(_vm.OpenCommand.CanExecute(null));
    }

    [Fact]
    public void Initially_SettingsMessagesAreShownInStatusBar()
    {
        File.WriteAllText(SettingsPath, "{ broken");

        var vm = CreateViewModel();

        Assert.Equal(Messages.M20_SettingsLoadFailed, vm.StatusText);
        Assert.Contains(Messages.M20_SettingsLoadFailed, File.ReadAllText(LogPath));
    }

    // ---------------------------------------------------------------- 開けたとき

    [Fact]
    public async Task Load_AddsDocumentWithoutSelection()
    {
        _fake.Image = ImageBuffer.Create(6000, 2000, new byte[6000 * 2000 * 4]);
        var path = CreateFile("photo.fake", bytes: 4321);

        await _vm.LoadAsync(path);

        var document = Assert.Single(_vm.Documents);
        Assert.Same(document, _vm.ActiveDocument);
        Assert.True(_vm.HasImage);
        Assert.Equal(path, document.Path);
        Assert.Equal("photo.fake", document.FileName);
        Assert.Equal(4321, document.SourceFileSize);
        Assert.Same(_fake.Image, document.Image);
        Assert.Null(document.Selection); // 開いただけでは、選択範囲（切り抜きの枠）は無い
        Assert.False(document.History.CanUndo);
        Assert.False(document.HasUnexportedEdits);
        Assert.Equal("photo.fake - webDeza", _vm.WindowTitle);
        Assert.Equal("画像: 6000×2000", _vm.StatusText);
        Assert.Empty(_dialogs.Errors);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task Load_NotifiesTitleAndStatusText()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await _vm.LoadAsync(CreateFile("a.fake"));

        Assert.Contains(nameof(MainViewModel.ActiveDocument), changed);
        Assert.Contains(nameof(MainViewModel.WindowTitle), changed);
        Assert.Contains(nameof(MainViewModel.StatusText), changed);
        Assert.Contains(nameof(MainViewModel.HasImage), changed);
        // 読み込み中は Busy になり、終わると戻る
        Assert.Equal(2, changed.Count(p => p == nameof(MainViewModel.IsBusy)));
    }

    [Fact]
    public async Task Load_SecondImage_ReplacesDocumentAndClearsStatusMessage()
    {
        File.WriteAllText(SettingsPath, "{ broken");
        var vm = CreateViewModel();
        await vm.LoadAsync(CreateFile("a.fake"));
        var second = ImageBuffer.Create(8, 4, new byte[8 * 4 * 4]);
        _fake.Image = second;

        await vm.LoadAsync(CreateFile("b.fake"));

        // 今は一覧に 1 件まで（複数は 7-3 から）
        var document = Assert.Single(vm.Documents);
        Assert.Equal("b.fake", document.FileName);
        Assert.Same(second, document.Image);
        Assert.Equal("b.fake - webDeza", vm.WindowTitle);
        Assert.Equal("画像: 8×4", vm.StatusText); // 起動時の M20 は消える
    }

    [Fact]
    public async Task Load_RemembersFolderInSettings()
    {
        var path = CreateFile("a.fake");

        await _vm.LoadAsync(path);

        Assert.Equal(_temp.Path, _vm.Settings.LastOpenFolder);
        // 設定ファイルにも保存されている
        var saved = new SettingsService(_formats, SettingsPath).Load().Settings;
        Assert.Equal(_temp.Path, saved.LastOpenFolder);
    }

    // ---------------------------------------------------------------- 表示用画像

    [Fact]
    public async Task Load_SmallImage_DisplaysAtSameSize()
    {
        _fake.Image = ImageBuffer.Create(30, 20, new byte[30 * 20 * 4]);

        await _vm.LoadAsync(CreateFile("a.fake"));

        var display = _vm.ActiveDocument!.Display;
        Assert.Same(_fake.Image, display.Image); // 縮小が要らなければ、そのまま使う
        Assert.Equal((30, 20), (display.ImageWidth, display.ImageHeight));
        Assert.Equal((30, 20), (display.Bitmap.PixelWidth, display.Bitmap.PixelHeight));
        Assert.True(display.Bitmap.IsFrozen); // バックグラウンドで作って画面のスレッドで使う
        Assert.False(display.IsOpaque); // 0 で埋めた画像はアルファも 0
    }

    [Fact]
    public async Task Load_LargeImage_DisplaysShrunkImageWithFullResolutionSize()
    {
        _fake.Image = ImageBuffer.Create(6000, 2000, new byte[6000 * 2000 * 4]);

        await _vm.LoadAsync(CreateFile("a.fake"));

        // 表示用画像は長辺 4096 px 以下に縮小し、選択範囲の座標系はフル解像度のまま
        var document = _vm.ActiveDocument!;
        Assert.Equal((4096, 1365), (document.Display.Bitmap.PixelWidth, document.Display.Bitmap.PixelHeight));
        Assert.Equal((6000, 2000), (document.Display.ImageWidth, document.Display.ImageHeight));
        Assert.Equal((6000, 2000), (document.ImageWidth, document.ImageHeight));
    }

    [Fact]
    public async Task Load_KeepsTransparency()
    {
        var pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < pixels.Length; i += 4) pixels[i + 3] = (byte)(i < 32 ? 255 : 128);
        _fake.Image = ImageBuffer.Create(4, 4, pixels);

        await _vm.LoadAsync(CreateFile("a.fake"));

        // 読み込み時に背景色で塗らない（キャンバスが市松模様の上に描く）
        var display = _vm.ActiveDocument!.Display;
        Assert.False(display.IsOpaque);
        var bitmapPixels = new byte[4 * 4 * 4];
        display.Bitmap.CopyPixels(bitmapPixels, 16, 0);
        Assert.Equal(128, bitmapPixels[^1]);
    }

    // ---------------------------------------------------------------- 読めないとき

    [Fact]
    public async Task Load_UnsupportedExtension_ShowsM01AndDoesNotRead()
    {
        await _vm.LoadAsync(CreateFile("a.txt"));

        Assert.Equal(new[] { Messages.M01_UnsupportedFormat }, _dialogs.Errors);
        Assert.Empty(_vm.Documents);
        Assert.False(_vm.HasImage);
        Assert.Equal(0, _fake.DecodeCount);
    }

    [Fact]
    public async Task Load_ContentNotSupported_ShowsM01AndDoesNotAddDocument()
    {
        _fake.Throw = () => new UnsupportedImageFormatException(UnsupportedImageFormatException.DefaultMessage, "fake", null);

        await _vm.LoadAsync(CreateFile("a.fake"));

        Assert.Equal(new[] { Messages.M01_UnsupportedFormat }, _dialogs.Errors);
        Assert.Empty(_vm.Documents); // 読めなかった画像は開かない（設計書 F-01）
        Assert.False(_vm.HasImage);
        Assert.Equal("webDeza", _vm.WindowTitle);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task Load_FormatWithRequirement_ShowsM01FollowedByM21()
    {
        // 形式の Requirement は、形式レジストリが例外に入れる
        _needsExtension.Throw = () => new UnsupportedImageFormatException();

        await _vm.LoadAsync(CreateFile("a.heavy"));

        var expected = Messages.M01_UnsupportedFormat + "\n" + Messages.M21_Requirement("Microsoft Store の「重い画像拡張機能」");
        Assert.Equal(new[] { expected }, _dialogs.Errors);
    }

    [Fact]
    public async Task Load_OtherNotSupportedException_ShowsM01()
    {
        _fake.Throw = () => new NotSupportedException("codec missing");

        await _vm.LoadAsync(CreateFile("a.fake"));

        Assert.Equal(new[] { Messages.M01_UnsupportedFormat }, _dialogs.Errors);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("too large")]
    [InlineData("oom")]
    [InlineData("plugin")]
    public async Task Load_OtherFailures_ShowM02(string kind)
    {
        Exception exception = kind switch
        {
            "broken" => new FileFormatException("ファイルが壊れています。"),
            "too large" => new ImageTooLargeException(40000, 40000),
            "oom" => new OutOfMemoryException(),
            _ => new InvalidOperationException("プラグインの不具合"),
        };
        _fake.Throw = () => exception;

        await _vm.LoadAsync(CreateFile("a.fake"));

        var expected = Messages.M02_LoadFailed(kind == "oom" ? Messages.OutOfMemory : exception.Message);
        Assert.Equal(new[] { expected }, _dialogs.Errors);
        Assert.Empty(_vm.Documents);
        Assert.False(_vm.IsBusy);
        Assert.Contains("Load", File.ReadAllText(LogPath)); // ログに書く
    }

    [Fact]
    public async Task Load_MissingFile_ShowsM02()
    {
        await _vm.LoadAsync(_temp.Combine("missing.fake"));

        var error = Assert.Single(_dialogs.Errors);
        Assert.StartsWith("画像を読み込めませんでした。（", error);
        Assert.Empty(_vm.Documents);
    }

    [Fact]
    public async Task Load_Failure_KeepsPreviousImage()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var previous = _vm.ActiveDocument;
        _fake.Throw = () => new FileFormatException("壊れています");

        await _vm.LoadAsync(CreateFile("b.fake"));

        // 読めなかったときは何も変えない
        Assert.Same(previous, _vm.ActiveDocument);
        Assert.Equal("a.fake", Assert.Single(_vm.Documents).FileName);
    }

    // ---------------------------------------------------------------- ［開く］

    [Fact]
    public async Task Open_UsesFilterFromRegistryAndLastFolder()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        _dialogs.PathToOpen = CreateFile("b.fake");

        await _vm.OpenCommand.ExecuteAsync(null);

        var (folder, filter) = _dialogs.OpenRequests.Single();
        Assert.Equal(_temp.Path, folder);
        Assert.Equal(OpenFileFilter.Build(_formats), filter);
        Assert.Equal("b.fake - webDeza", _vm.WindowTitle);
    }

    [Fact]
    public async Task Open_Cancelled_DoesNothing()
    {
        _dialogs.PathToOpen = null;

        await _vm.OpenCommand.ExecuteAsync(null);

        Assert.Empty(_vm.Documents);
        Assert.Empty(_dialogs.Errors);
        Assert.Equal(0, _fake.DecodeCount);
    }

    [Fact]
    public async Task Open_IsDisabledWhileLoading()
    {
        var gate = new ManualResetEventSlim();
        _fake.OnDecode = () => gate.Wait(TimeSpan.FromSeconds(10));
        var loading = _vm.LoadAsync(CreateFile("a.fake"));

        Assert.True(_vm.IsBusy);
        Assert.False(_vm.OpenCommand.CanExecute(null));
        await _vm.LoadAsync(CreateFile("b.fake")); // 読み込み中に頼まれた分は無視する
        gate.Set();
        await loading;

        Assert.False(_vm.IsBusy);
        Assert.True(_vm.OpenCommand.CanExecute(null));
        Assert.Equal("a.fake", Assert.Single(_vm.Documents).FileName);
        Assert.Equal(1, _fake.DecodeCount);
    }
}
