using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Models;
using WebDeza.Services;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>画像を開く（実装計画書 5-4）。形式とダイアログを差し替えて、ファイルの中身や画面を使わずに確かめる</summary>
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
        => new(_formats, new FilterRegistry(), new SettingsService(_formats, SettingsPath), _dialogs, new AppLog(LogPath));

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
        Assert.Null(_vm.Display);
        Assert.Equal("webDeza", _vm.WindowTitle);
        Assert.Equal("", _vm.StatusText);
        Assert.Empty(_vm.Items);
        Assert.Null(_vm.SelectedItem);
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

    // ---------------------------------------------------------------- 読み込めたとき

    [Fact]
    public async Task Load_ChangesTitleAndImageSize()
    {
        _fake.Image = ImageBuffer.Create(6000, 2000, new byte[6000 * 2000 * 4]);
        var path = CreateFile("photo.fake", bytes: 4321);

        await _vm.LoadAsync(path);

        Assert.True(_vm.HasImage);
        Assert.Same(_fake.Image, _vm.SourceImage);
        Assert.Equal("photo.fake - webDeza", _vm.WindowTitle);
        Assert.Equal("画像: 6000×2000", _vm.StatusText);
        Assert.Empty(_dialogs.Errors);

        var item = Assert.Single(_vm.Items);
        Assert.Same(item, _vm.SelectedItem);
        Assert.Equal(path, item.Path);
        Assert.Equal("photo.fake", item.FileName);
        Assert.Equal(4321, item.SourceFileSize);
        Assert.Equal(ImageItemStatus.NotExported, item.Status);
        Assert.Equal(EditRecipe.Initial, item.History.Current);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task Load_NotifiesTitleAndStatusText()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await _vm.LoadAsync(CreateFile("a.fake"));

        Assert.Contains(nameof(MainViewModel.WindowTitle), changed);
        Assert.Contains(nameof(MainViewModel.StatusText), changed);
        Assert.Contains(nameof(MainViewModel.HasImage), changed);
        // 読み込み中は Busy になり、終わると戻る
        Assert.Equal(2, changed.Count(p => p == nameof(MainViewModel.IsBusy)));
    }

    [Fact]
    public async Task Load_SecondImage_ReplacesListAndClearsStatusMessage()
    {
        File.WriteAllText(SettingsPath, "{ broken");
        var vm = CreateViewModel();
        await vm.LoadAsync(CreateFile("a.fake"));
        var second = ImageBuffer.Create(8, 4, new byte[8 * 4 * 4]);
        _fake.Image = second;

        await vm.LoadAsync(CreateFile("b.fake"));

        // 今は一覧に 1 件まで（複数は 9-2 から）
        var item = Assert.Single(vm.Items);
        Assert.Equal("b.fake", item.FileName);
        Assert.Same(second, vm.SourceImage);
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

    // ---------------------------------------------------------------- 読めないとき

    [Fact]
    public async Task Load_UnsupportedExtension_ShowsM01AndDoesNotAddItem()
    {
        await _vm.LoadAsync(CreateFile("a.txt"));

        Assert.Equal(new[] { Messages.M01_UnsupportedFormat }, _dialogs.Errors);
        Assert.Empty(_vm.Items);
        Assert.False(_vm.HasImage);
        Assert.Equal(0, _fake.DecodeCount);
    }

    [Fact]
    public async Task Load_ContentNotSupported_ShowsM01AndMarksItemAsError()
    {
        _fake.Throw = () => new UnsupportedImageFormatException(UnsupportedImageFormatException.DefaultMessage, "fake", null);

        await _vm.LoadAsync(CreateFile("a.fake"));

        Assert.Equal(new[] { Messages.M01_UnsupportedFormat }, _dialogs.Errors);
        var item = Assert.Single(_vm.Items); // 失敗した画像も一覧に残す
        Assert.Equal(ImageItemStatus.Error, item.Status);
        Assert.Equal(Messages.M01_UnsupportedFormat, item.ErrorMessage);
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
        Assert.Equal(expected, _vm.Items[0].ErrorMessage);
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
        Assert.Equal(ImageItemStatus.Error, _vm.Items[0].Status);
        Assert.False(_vm.HasImage);
        Assert.False(_vm.IsBusy);
        Assert.Contains("Load", File.ReadAllText(LogPath)); // ログに書く
    }

    [Fact]
    public async Task Load_MissingFile_ShowsM02()
    {
        await _vm.LoadAsync(_temp.Combine("missing.fake"));

        var error = Assert.Single(_dialogs.Errors);
        Assert.StartsWith("画像を読み込めませんでした。（", error);
        Assert.Equal(ImageItemStatus.Error, _vm.Items[0].Status);
    }

    [Fact]
    public async Task Load_AfterFailure_PreviousImageIsReleased()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        _fake.Throw = () => new FileFormatException("壊れています");

        await _vm.LoadAsync(CreateFile("b.fake"));

        Assert.Null(_vm.SourceImage); // 前の画像への参照を外している
        Assert.Null(_vm.Display);
        Assert.Equal("b.fake", Assert.Single(_vm.Items).FileName);
    }

    // ---------------------------------------------------------------- 表示用画像（5-5）

    [Fact]
    public async Task Load_SmallImage_DisplaysAtSameSize()
    {
        _fake.Image = ImageBuffer.Create(30, 20, new byte[30 * 20 * 4]);

        await _vm.LoadAsync(CreateFile("a.fake"));

        var display = Assert.IsType<DisplayImage>(_vm.Display);
        Assert.Same(_fake.Image, display.Preview.Image); // 縮小もフィルターも要らなければ、そのまま使う
        Assert.Equal((30, 20), (display.EditedWidth, display.EditedHeight));
        Assert.Equal((30, 20), (display.Bitmap.PixelWidth, display.Bitmap.PixelHeight));
        Assert.True(display.Bitmap.IsFrozen); // バックグラウンドで作って画面のスレッドで使う
        Assert.False(display.IsOpaque); // 0 で埋めた画像はアルファも 0
    }

    [Fact]
    public async Task Load_LargeImage_DisplaysShrunkImageWithFullResolutionSize()
    {
        _fake.Image = ImageBuffer.Create(6000, 2000, new byte[6000 * 2000 * 4]);

        await _vm.LoadAsync(CreateFile("a.fake"));

        // 表示用画像は長辺 4096 px 以下に縮小し、切り抜きの座標系はフル解像度のまま
        var display = _vm.Display!;
        Assert.Equal((4096, 1365), (display.Bitmap.PixelWidth, display.Bitmap.PixelHeight));
        Assert.Equal((6000, 2000), (display.EditedWidth, display.EditedHeight));
        Assert.Same(_fake.Image, _vm.SourceImage); // 元画像はフル解像度のまま持つ（書き出しに使う）
    }

    [Fact]
    public async Task Load_KeepsTransparency()
    {
        var pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < pixels.Length; i += 4) pixels[i + 3] = (byte)(i < 32 ? 255 : 128);
        _fake.Image = ImageBuffer.Create(4, 4, pixels);

        await _vm.LoadAsync(CreateFile("a.fake"));

        // 読み込み時に背景色で塗らない（キャンバスが市松模様の上に描く）
        Assert.False(_vm.Display!.IsOpaque);
        var bitmapPixels = new byte[4 * 4 * 4];
        _vm.Display.Bitmap.CopyPixels(bitmapPixels, 16, 0);
        Assert.Equal(128, bitmapPixels[^1]);
    }

    [Fact]
    public async Task Load_EachImageRequestsResetView()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        Assert.Equal(1, _vm.ResetViewRequest);

        await _vm.LoadAsync(CreateFile("b.fake"));
        Assert.Equal(2, _vm.ResetViewRequest);

        // 読めなかったときは表示する画像が無いので、増やさない
        _fake.Throw = () => new FileFormatException("x");
        await _vm.LoadAsync(CreateFile("c.fake"));
        Assert.Equal(2, _vm.ResetViewRequest);
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

        Assert.Empty(_vm.Items);
        Assert.Empty(_dialogs.Errors);
        Assert.Equal(0, _fake.DecodeCount);
    }

    [Fact]
    public async Task Open_IsDisabledWhileLoading()
    {
        var gate = new ManualResetEventSlim();
        _fake.Throw = () => { gate.Wait(TimeSpan.FromSeconds(10)); return new FileFormatException("x"); };
        var loading = _vm.LoadAsync(CreateFile("a.fake"));

        Assert.True(_vm.IsBusy);
        Assert.False(_vm.OpenCommand.CanExecute(null));
        await _vm.LoadAsync(CreateFile("b.fake")); // 読み込み中に頼まれた分は無視する
        gate.Set();
        await loading;

        Assert.False(_vm.IsBusy);
        Assert.True(_vm.OpenCommand.CanExecute(null));
        Assert.Equal("a.fake", Assert.Single(_vm.Items).FileName);
    }
}
