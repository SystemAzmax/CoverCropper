using System.IO;
using WebDeza.Core;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Services;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>
/// 複数の画像を開く・切り替える・閉じる・並べる（設計書 F-01・F-02。V38・V39 の一部。実装計画書 7-3・7-4）。
/// 形式とダイアログを差し替えて、ファイルの中身や画面を使わずに確かめる
/// </summary>
public sealed class MainViewModelDocumentsTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeFormat _fake = new("fake", ".fake");
    private readonly FakeFormat _broken = new("broken", ".broken") { Throw = () => new FileFormatException("壊れています") };
    private readonly FormatRegistry _formats = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly MainViewModel _vm;

    public MainViewModelDocumentsTests()
    {
        _formats.AddWicFormats();
        _formats.Register(_fake);
        _formats.Register(_broken);
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

    private string[] CreateFiles(params string[] names) => names.Select(CreateFile).ToArray();

    private string[] FileNames => _vm.Documents.Select(d => d.FileName).ToArray();

    // ---------------------------------------------------------------- V38: 3 枚を開く

    [Fact]
    public async Task Open_ThreeFiles_AllAddedAndLastIsActive()
    {
        _dialogs.PathsToOpen = CreateFiles("a.fake", "b.fake", "c.fake");

        await _vm.OpenCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "a.fake", "b.fake", "c.fake" }, FileNames);
        Assert.Equal("c.fake", _vm.ActiveDocument!.FileName);
        Assert.Equal("c.fake - webDeza", _vm.WindowTitle);
        Assert.Empty(_dialogs.Errors);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task Open_AddsToAlreadyOpenDocuments()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));

        await _vm.OpenFilesAsync(CreateFiles("b.fake", "c.fake"));

        Assert.Equal(new[] { "a.fake", "b.fake", "c.fake" }, FileNames);
        Assert.Equal("c.fake", _vm.ActiveDocument!.FileName);
    }

    // ---------------------------------------------------------------- V38: 開いている画像をもう一度開く

    [Fact]
    public async Task Open_AlreadyOpen_IsNotReopenedAndBecomesActive()
    {
        var paths = CreateFiles("a.fake", "b.fake");
        await _vm.OpenFilesAsync(paths);
        var first = _vm.Documents[0];

        // 大文字小文字だけが違うパスも、同じ画像とみなす
        await _vm.LoadAsync(paths[0].ToUpperInvariant());

        Assert.Equal(new[] { "a.fake", "b.fake" }, FileNames);
        Assert.Same(first, _vm.ActiveDocument);
        Assert.Equal(2, _fake.DecodeCount);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task Open_DuplicatesInOneRequest_OpenedOnce()
    {
        var a = CreateFile("a.fake");

        await _vm.OpenFilesAsync([a, a.ToUpperInvariant(), Path.Combine(_temp.Path, ".", "a.fake")]);

        Assert.Equal(new[] { "a.fake" }, FileNames);
        Assert.Equal(1, _fake.DecodeCount);
    }

    [Fact]
    public async Task Open_LastRequestedIsAlreadyOpen_ThatOneBecomesActive()
    {
        var a = CreateFile("a.fake");
        await _vm.LoadAsync(a);
        var first = _vm.ActiveDocument;

        await _vm.OpenFilesAsync([CreateFile("b.fake"), a]);

        Assert.Equal(new[] { "a.fake", "b.fake" }, FileNames);
        Assert.Same(first, _vm.ActiveDocument);
    }

    // ---------------------------------------------------------------- V38: 上限を超える

    [Fact]
    public async Task Open_OverLimit_RestAreNotOpenedAndM15()
    {
        var names = Enumerable.Range(1, MainViewModel.MaxDocuments - 1).Select(i => $"{i:D2}.fake").ToArray();
        await _vm.OpenFilesAsync(CreateFiles(names));

        await _vm.OpenFilesAsync(CreateFiles("x.fake", "y.fake", "z.fake"));

        // 1 枚だけ開け、2 件は開かない
        Assert.Equal(MainViewModel.MaxDocuments, _vm.Documents.Count);
        Assert.Equal("x.fake", _vm.ActiveDocument!.FileName);
        Assert.EndsWith(Messages.M15_OverLimit(MainViewModel.MaxDocuments, 2), _vm.StatusText);
        Assert.Equal(MainViewModel.MaxDocuments, _fake.DecodeCount);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task Open_AtLimit_AlreadyOpenCanStillBeSelected()
    {
        var names = Enumerable.Range(1, MainViewModel.MaxDocuments).Select(i => $"{i:D2}.fake").ToArray();
        var paths = CreateFiles(names);
        await _vm.OpenFilesAsync(paths);

        await _vm.LoadAsync(paths[0]);

        Assert.Same(_vm.Documents[0], _vm.ActiveDocument);
        Assert.Equal("画像: 30×20", _vm.StatusText); // M15 は出さない
    }

    // ---------------------------------------------------------------- 読めなかったもの（M29）

    [Fact]
    public async Task Open_SomeFail_OpensReadableOnesAndReportsFailuresOnce()
    {
        await _vm.OpenFilesAsync(CreateFiles("a.fake", "b.broken", "c.txt", "d.fake"));

        Assert.Equal(new[] { "a.fake", "d.fake" }, FileNames);
        Assert.Equal("d.fake", _vm.ActiveDocument!.FileName);
        var error = Assert.Single(_dialogs.Errors);
        Assert.Equal(Messages.M29_OpenFailed(
        [
            ("b.broken", Messages.M02_LoadFailed("壊れています")),
            ("c.txt", Messages.M01_UnsupportedFormat),
        ]), error);
    }

    [Fact]
    public async Task Open_AllFail_KeepsActiveDocumentAndReportsWithM29()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var active = _vm.ActiveDocument;

        await _vm.OpenFilesAsync(CreateFiles("b.broken", "c.broken"));

        Assert.Same(active, _vm.ActiveDocument);
        Assert.Single(_vm.Documents);
        Assert.StartsWith("2 件を開けませんでした。", Assert.Single(_dialogs.Errors));
    }

    [Fact]
    public async Task Open_SeveralFiles_IsBusyOnlyOnceAndRemembersLastFolder()
    {
        var sub = Directory.CreateDirectory(_temp.Combine("sub")).FullName;
        File.WriteAllBytes(Path.Combine(sub, "b.fake"), new byte[16]);
        var busyChanges = 0;
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.IsBusy)) busyChanges++; };

        await _vm.OpenFilesAsync([CreateFile("a.fake"), Path.Combine(sub, "b.fake")]);

        Assert.Equal(2, busyChanges); // 開き始めと終わりだけ（メニューなどが 1 枚ごとにちらつかない）
        Assert.Equal(sub, _vm.Settings.LastOpenFolder);
    }

    // ---------------------------------------------------------------- 切り替え（Ctrl+Tab / Ctrl+Shift+Tab）

    [Fact]
    public async Task NextAndPrevious_CycleInOpenedOrder()
    {
        await _vm.OpenFilesAsync(CreateFiles("a.fake", "b.fake", "c.fake"));
        Assert.Equal("c.fake", _vm.ActiveDocument!.FileName);

        _vm.NextDocumentCommand.Execute(null);
        Assert.Equal("a.fake", _vm.ActiveDocument!.FileName); // 最後の次は最初

        _vm.NextDocumentCommand.Execute(null);
        Assert.Equal("b.fake", _vm.ActiveDocument!.FileName);

        _vm.PreviousDocumentCommand.Execute(null);
        _vm.PreviousDocumentCommand.Execute(null);
        Assert.Equal("c.fake", _vm.ActiveDocument!.FileName); // 最初の前は最後
        Assert.Equal("c.fake - webDeza", _vm.WindowTitle);
    }

    [Fact]
    public async Task NextAndPrevious_NeedTwoOrMoreDocuments()
    {
        Assert.False(_vm.NextDocumentCommand.CanExecute(null));
        await _vm.LoadAsync(CreateFile("a.fake"));
        Assert.False(_vm.NextDocumentCommand.CanExecute(null));
        Assert.False(_vm.PreviousDocumentCommand.CanExecute(null));

        await _vm.LoadAsync(CreateFile("b.fake"));

        Assert.True(_vm.NextDocumentCommand.CanExecute(null));
        Assert.True(_vm.PreviousDocumentCommand.CanExecute(null));
    }

    [Fact]
    public async Task StatusText_FollowsActiveDocument()
    {
        _fake.Image = WebDeza.Imaging.ImageBuffer.Create(10, 5, new byte[10 * 5 * 4]);
        await _vm.LoadAsync(CreateFile("a.fake"));
        _fake.Image = WebDeza.Imaging.ImageBuffer.Create(7, 3, new byte[7 * 3 * 4]);
        await _vm.LoadAsync(CreateFile("b.fake"));
        var first = _vm.Documents[0];
        var second = _vm.Documents[1];

        _vm.ActiveDocument = first;
        Assert.Equal("画像: 10×5", _vm.StatusText);

        // アクティブでない画像の選択範囲が変わっても、ステータスバーは変わらない
        second.Selection = new WebDeza.Imaging.PixelRect(0, 0, 2, 2);
        Assert.Equal("画像: 10×5", _vm.StatusText);

        _vm.ActiveDocument = second;
        Assert.Equal("画像: 7×3 ／ 選択範囲: (0, 0) 2×2", _vm.StatusText);
    }

    // ---------------------------------------------------------------- V39 の一部: アクティブな画像を閉じる

    [Fact]
    public async Task CloseActive_NextBecomesActive()
    {
        await _vm.OpenFilesAsync(CreateFiles("a.fake", "b.fake", "c.fake"));
        _vm.ActiveDocument = _vm.Documents[1];

        _vm.CloseCommand.Execute(null);

        Assert.Equal(new[] { "a.fake", "c.fake" }, FileNames);
        Assert.Equal("c.fake", _vm.ActiveDocument!.FileName);
    }

    [Fact]
    public async Task CloseActive_LastOne_PreviousBecomesActive()
    {
        await _vm.OpenFilesAsync(CreateFiles("a.fake", "b.fake", "c.fake"));

        _vm.CloseCommand.Execute(null); // c（最後）を閉じる

        Assert.Equal(new[] { "a.fake", "b.fake" }, FileNames);
        Assert.Equal("b.fake", _vm.ActiveDocument!.FileName);
    }

    [Fact]
    public async Task CloseInactive_ActiveDoesNotChange()
    {
        await _vm.OpenFilesAsync(CreateFiles("a.fake", "b.fake", "c.fake"));
        var active = _vm.ActiveDocument;

        _vm.CloseCommand.Execute(_vm.Documents[0]); // 子ウィンドウの［閉じる］は、アクティブでない画像も閉じられる

        Assert.Equal(new[] { "b.fake", "c.fake" }, FileNames);
        Assert.Same(active, _vm.ActiveDocument);
    }

    // ---------------------------------------------------------------- アクティブの印（7-4）

    [Fact]
    public async Task IsActive_FollowsActiveDocument()
    {
        await _vm.OpenFilesAsync(CreateFiles("a.fake", "b.fake"));
        var (a, b) = (_vm.Documents[0], _vm.Documents[1]);
        Assert.Equal((false, true), (a.IsActive, b.IsActive));

        _vm.ActiveDocument = a;
        Assert.Equal((true, false), (a.IsActive, b.IsActive));

        _vm.CloseCommand.Execute(a);
        Assert.False(a.IsActive); // 閉じた画像の印は外れる
        Assert.True(b.IsActive);
    }

    // ---------------------------------------------------------------- メニュー［ウィンドウ］の一覧（7-4）

    [Fact]
    public async Task ActivateDocument_MakesThatDocumentActive()
    {
        await _vm.OpenFilesAsync(CreateFiles("a.fake", "b.fake"));
        var first = _vm.Documents[0];

        Assert.True(_vm.ActivateDocumentCommand.CanExecute(first));
        _vm.ActivateDocumentCommand.Execute(first);

        Assert.Same(first, _vm.ActiveDocument);
        Assert.Equal("a.fake - webDeza", _vm.WindowTitle);
    }

    [Fact]
    public async Task ActivateDocument_ClosedDocument_IsIgnored()
    {
        await _vm.OpenFilesAsync(CreateFiles("a.fake", "b.fake"));
        var closed = _vm.Documents[0];
        _vm.CloseCommand.Execute(closed);

        Assert.False(_vm.ActivateDocumentCommand.CanExecute(closed));
        _vm.ActivateDocumentCommand.Execute(closed);

        Assert.Equal("b.fake", _vm.ActiveDocument!.FileName);
    }

    [Theory]
    [InlineData(0, "photo.jpg", "_1: photo.jpg")]
    [InlineData(8, "photo.jpg", "_9: photo.jpg")]
    [InlineData(9, "photo.jpg", "10: photo.jpg")]                  // 10 番目からはアクセスキーにしない
    [InlineData(1, "my_photo.jpg", "_2: my__photo.jpg")]           // 「_」はアクセスキーの印と見なされないようにする
    [InlineData(0, "202300119-a.jpg", "_1: 202300119-a.jpg")]     // 数字で始まるファイル名も、番号とコロンで区切る
    public void WindowListHeader_NumberAndEscapedName(int index, string fileName, string expected)
    {
        Assert.Equal(expected, WebDeza.Views.MainWindow.WindowListHeader(index, fileName));
    }

    // ---------------------------------------------------------------- 重ねて表示・並べて表示（7-4）

    [Fact]
    public async Task CascadeAndTile_RequestArrangement()
    {
        var requests = new List<WindowArrangement>();
        _vm.ArrangeWindowsRequested += (_, arrangement) => requests.Add(arrangement);
        await _vm.LoadAsync(CreateFile("a.fake"));

        Assert.True(_vm.CascadeWindowsCommand.CanExecute(null));
        Assert.True(_vm.TileWindowsCommand.CanExecute(null));
        _vm.CascadeWindowsCommand.Execute(null);
        _vm.TileWindowsCommand.Execute(null);

        Assert.Equal(new[] { WindowArrangement.Cascade, WindowArrangement.Tile }, requests);
    }

    // ---------------------------------------------------------------- すべて閉じる（7-4）

    [Fact]
    public async Task CloseAll_ClosesEveryDocumentButKeepsFiles()
    {
        var paths = CreateFiles("a.fake", "b.fake", "c.fake");
        await _vm.OpenFilesAsync(paths);
        var documents = _vm.Documents.ToList();

        Assert.True(_vm.CloseAllCommand.CanExecute(null));
        _vm.CloseAllCommand.Execute(null);

        Assert.Empty(_vm.Documents);
        Assert.Null(_vm.ActiveDocument);
        Assert.Equal("webDeza", _vm.WindowTitle);
        Assert.All(documents, d => Assert.False(d.IsActive));
        Assert.All(paths, p => Assert.True(File.Exists(p)));
        Assert.False(_vm.CloseAllCommand.CanExecute(null));
        Assert.False(_vm.TileWindowsCommand.CanExecute(null));
    }

    [Fact]
    public async Task WindowCommands_DisabledWhileLoading()
    {
        await _vm.LoadAsync(CreateFile("a.fake"));
        var gate = new ManualResetEventSlim();
        _fake.OnDecode = () => gate.Wait(TimeSpan.FromSeconds(10));

        var loading = _vm.LoadAsync(CreateFile("b.fake"));

        Assert.False(_vm.CloseAllCommand.CanExecute(null));
        Assert.False(_vm.CascadeWindowsCommand.CanExecute(null));
        Assert.False(_vm.TileWindowsCommand.CanExecute(null));
        Assert.False(_vm.ActivateDocumentCommand.CanExecute(_vm.Documents[0]));
        gate.Set();
        await loading;

        Assert.True(_vm.CloseAllCommand.CanExecute(null));
        Assert.True(_vm.TileWindowsCommand.CanExecute(null));
    }
}
