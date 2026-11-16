using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Wic;
using WebDeza.Imaging.Formats;
using WebDeza.Models;
using WebDeza.Services;
using WebDeza.ViewModels;
using WebDeza.Views.Canvas;

namespace WebDeza.Tests.ViewModels;

/// <summary>開いている画像 1 枚と、選択範囲の窓口（実装計画書 6-5）</summary>
public sealed class DocumentViewModelTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeDialogs _dialogs = new();

    public void Dispose() => _temp.Dispose();

    private string LogFile => _temp.Combine("logs", "app.log");

    private static ImageBuffer Blank(int width, int height) => ImageBuffer.Create(width, height, new byte[width * height * 4]);

    /// <summary>画素ごとに違う値を持つ画像</summary>
    private static ImageBuffer Pattern(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7 % 251);
        return ImageBuffer.Create(width, height, pixels);
    }

    private DocumentViewModel Create(int width = 600, int height = 400, AspectRatio? aspect = null)
        => Create(Blank(width, height), aspect);

    private DocumentViewModel Create(ImageBuffer image, AspectRatio? aspect = null)
        => new(@"C:\images\photo.png", image, DisplayImage.Create(image), 1234, (_, _) => aspect, _dialogs, new AppLog(LogFile));

    // ---------------------------------------------------------------- 最初の状態

    [Fact]
    public void Initially_NoSelectionAndNoEdits()
    {
        var document = Create();

        Assert.Null(document.Selection);
        Assert.Equal((600, 400), (document.ImageWidth, document.ImageHeight));
        Assert.Equal("photo.png", document.FileName);
        Assert.Equal(1234, document.SourceFileSize);
        Assert.False(document.HasUnexportedEdits);
        Assert.Equal(0, document.ResetViewRequest);
        Assert.Same(document.History.Current, document.Image);
        Assert.Equal("photo.png", document.Title);
        Assert.False(document.IsBusy);
    }

    [Fact]
    public void Constructor_DisplayOfOtherSize_Throws()
    {
        var image = Blank(10, 10);

        Assert.Throws<ArgumentException>(() =>
            new DocumentViewModel("a.png", image, DisplayImage.Create(Blank(5, 5)), 0, (_, _) => null, _dialogs, new AppLog(LogFile)));
    }

    // ---------------------------------------------------------------- 選択範囲

    [Fact]
    public void RefitSelection_FitsToCurrentAspect_AndRaisesChanged()
    {
        // 比率（全画像共通の設定）が変わったときに MainViewModel が呼ぶ（実装計画書 8-8）
        AspectRatio? aspect = null;
        var image = Blank(600, 400);
        var document = new DocumentViewModel("a.png", image, DisplayImage.Create(image), 0, (_, _) => aspect, _dialogs, new AppLog(LogFile));
        document.Selection = new PixelRect(100, 100, 200, 50);
        int changed = 0;
        document.Changed += (_, _) => changed++;

        aspect = new AspectRatio(1, 1);
        document.RefitSelection();
        Assert.Equal(new PixelRect(150, 75, 100, 100), document.Selection); // 中心と面積を保って正方形に
        Assert.True(changed > 0);

        document.Selection = null;
        changed = 0;
        document.RefitSelection();
        Assert.Equal(1, changed); // 選択範囲が無くても描き直しを知らせる（辺のハンドルの有無が変わるため）
    }

    [Fact]
    public void Selection_RaisesChangedAndPropertyChanged()
    {
        var document = Create();
        int changed = 0;
        var properties = new List<string?>();
        document.Changed += (_, _) => changed++;
        document.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        document.Selection = new PixelRect(10, 20, 100, 50);

        Assert.Equal(new PixelRect(10, 20, 100, 50), document.Selection);
        Assert.Equal(1, changed);
        Assert.Contains(nameof(DocumentViewModel.Selection), properties);

        // 同じ値なら知らせない
        document.Selection = new PixelRect(10, 20, 100, 50);
        Assert.Equal(1, changed);

        document.Selection = null;
        Assert.Null(document.Selection);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Selection_OutsideImage_IsClampedInside()
    {
        var document = Create();

        document.Selection = new PixelRect(550, -30, 100, 50);
        Assert.Equal(new PixelRect(500, 0, 100, 50), document.Selection);

        document.Selection = new PixelRect(-10, -10, 9999, 9999);
        Assert.Equal(new PixelRect(0, 0, 600, 400), document.Selection);
    }

    [Fact]
    public void Selection_FollowsAspectFromSettings()
    {
        var document = Create(aspect: new AspectRatio(1, 1));

        document.Selection = new PixelRect(10, 10, 200, 50);

        // 比率が 1:1 なら、幅から高さを決める
        Assert.Equal(new PixelRect(10, 10, 200, 200), document.Selection);
        Assert.Equal(new AspectRatio(1, 1), document.SelectionRules.Aspect);
    }

    [Fact]
    public void Selection_IsTheSessionForCanvas()
    {
        IEditorSession session = Create();

        session.Selection = new PixelRect(1, 2, 3, 4);

        Assert.Equal(new PixelRect(1, 2, 3, 4), session.Selection);
        Assert.Equal((600, 400), (session.ImageWidth, session.ImageHeight));
        Assert.Null(session.SelectionRules.Aspect);
    }

    [Fact]
    public void DisplayChange_RaisesChanged()
    {
        var document = Create();
        int changed = 0;
        document.Changed += (_, _) => changed++;

        document.Display = DisplayImage.Create(Blank(600, 400));

        Assert.Equal(1, changed);
    }

    // ---------------------------------------------------------------- 書き出していない編集

    [Fact]
    public void HasUnexportedEdits_FollowsHistoryState()
    {
        var document = Create();

        document.History.Push("編集", Blank(600, 400));
        Assert.True(document.HasUnexportedEdits);

        document.History.Undo();
        Assert.False(document.HasUnexportedEdits); // 開いた時点の状態に戻れば、書き出していない編集は無い
    }

    // ---------------------------------------------------------------- 編集の確定（V31。実装計画書 9-1）

    [Fact]
    public async Task ApplyEdit_Success_PushesHistoryAndRebuildsDisplay()
    {
        var document = Create(Pattern(60, 40));
        document.Selection = new PixelRect(10, 10, 20, 20);
        var before = (Image: document.Image, Display: document.Display);
        var properties = new List<string?>();
        int changed = 0;
        document.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        document.Changed += (_, _) => changed++;
        var edited = Blank(60, 40);

        await document.ApplyEditAsync("編集", (image, _) => image == before.Image ? edited : throw new InvalidOperationException(),
            changedRegion: null, newSelection: document.Selection, resetView: false);

        Assert.Same(edited, document.Image);
        Assert.NotSame(before.Display, document.Display);
        Assert.Same(edited, document.Display.Image); // 縮小が要らない大きさなら、画像そのものを表示する
        Assert.Equal(1, document.History.UndoCount);
        Assert.Equal("編集", document.History.UndoName);
        Assert.Equal(new PixelRect(10, 10, 20, 20), document.Selection); // フィルターのように、選択範囲は今のまま
        Assert.Equal(0, document.ResetViewRequest);
        Assert.True(document.HasUnexportedEdits);
        Assert.Equal("photo.png *", document.Title);
        Assert.False(document.IsBusy);
        Assert.Empty(_dialogs.Errors);
        Assert.Contains(nameof(DocumentViewModel.Image), properties);
        Assert.Contains(nameof(DocumentViewModel.HasUnexportedEdits), properties);
        Assert.Contains(nameof(DocumentViewModel.Title), properties);
        Assert.True(changed > 0); // キャンバスに描き直させる
    }

    [Fact]
    public async Task ApplyEdit_SizeChanged_ResetsViewAndSelection()
    {
        var document = Create(600, 400);
        document.Selection = new PixelRect(10, 20, 300, 200);

        await document.ApplyEditAsync("切り抜き", (_, _) => Blank(300, 200),
            changedRegion: null, newSelection: null, resetView: true);

        Assert.Equal((300, 200), (document.ImageWidth, document.ImageHeight));
        Assert.Equal((300, 200), (document.Display.ImageWidth, document.Display.ImageHeight));
        Assert.Null(document.Selection);
        Assert.Equal(1, document.ResetViewRequest);
    }

    [Fact]
    public async Task ApplyEdit_NewSelectionIsClampedIntoNewImage()
    {
        var document = Create(600, 400);

        await document.ApplyEditAsync("編集", (_, _) => Blank(100, 50),
            changedRegion: null, newSelection: new PixelRect(80, 40, 40, 20), resetView: true);

        Assert.Equal(new PixelRect(60, 30, 40, 20), document.Selection);
    }

    [Fact]
    public async Task ApplyEdit_WithChangedRegion_HistoryKeepsOnlyTheRegion()
    {
        // 選択範囲へのフィルターのように一部だけが変わる編集では、履歴がその範囲の画素だけを持つ
        var document = Create(600, 400);

        await document.ApplyEditAsync("編集", (_, _) => Pattern(600, 400),
            changedRegion: new PixelRect(0, 0, 10, 10), newSelection: null, resetView: false);

        Assert.True(document.History.StoredBytes < 600L * 400 * 4);
    }

    [Fact]
    public async Task ApplyEdit_Throws_KeepsImageAndHistory_AndShowsM28()
    {
        var document = Create(600, 400);
        document.Selection = new PixelRect(10, 20, 300, 200);
        var before = (Image: document.Image, Display: document.Display, StateId: document.History.StateId);
        bool busyWhenShown = true;
        _dialogs.OnShowError = _ => busyWhenShown = document.IsBusy;

        await document.ApplyEditAsync("編集", (_, _) => throw new InvalidOperationException("壊れた"),
            changedRegion: null, newSelection: null, resetView: true);

        Assert.Same(before.Image, document.Image);
        Assert.Same(before.Display, document.Display);
        Assert.Equal(before.StateId, document.History.StateId);
        Assert.False(document.History.CanUndo);
        Assert.Equal(new PixelRect(10, 20, 300, 200), document.Selection);
        Assert.Equal(0, document.ResetViewRequest);
        Assert.False(document.HasUnexportedEdits);
        Assert.Equal([Messages.M28_EditFailed("壊れた")], _dialogs.Errors);
        Assert.False(busyWhenShown); // 確定中（待機カーソル）を解いてから出す
        Assert.False(document.IsBusy);
        Assert.Contains("Edit failed", File.ReadAllText(LogFile));
    }

    [Fact]
    public async Task ApplyEdit_OutOfMemory_ShowsM28WithMemoryMessage()
    {
        var document = Create(60, 40);

        await document.ApplyEditAsync("編集", (_, _) => throw new OutOfMemoryException(),
            changedRegion: null, newSelection: null, resetView: false);

        Assert.Equal([Messages.M28_EditFailed(Messages.OutOfMemory)], _dialogs.Errors);
        Assert.False(document.History.CanUndo);
        Assert.Contains("Edit OOM", File.ReadAllText(LogFile));
    }

    [Fact]
    public async Task ApplyEdit_WhileBusy_IgnoresOtherEdits()
    {
        var document = Create(60, 40);
        var gate = new ManualResetEventSlim();
        var busy = new List<bool>();
        document.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.IsBusy)) busy.Add(document.IsBusy);
        };

        var first = document.ApplyEditAsync("1", (_, _) => { gate.Wait(TimeSpan.FromSeconds(10)); return Blank(60, 40); },
            changedRegion: null, newSelection: null, resetView: false);
        Assert.True(document.IsBusy);
        await document.ApplyEditAsync("2", (_, _) => Blank(30, 20), changedRegion: null, newSelection: null, resetView: true);
        gate.Set();
        await first;

        Assert.Equal([true, false], busy);
        Assert.Equal(1, document.History.UndoCount);
        Assert.Equal("1", document.History.UndoName);
        Assert.Equal((60, 40), (document.ImageWidth, document.ImageHeight));
    }

    // ---------------------------------------------------------------- MainViewModel との組み合わせ

    [Fact]
    public async Task MainViewModel_StatusTextShowsSelection()
    {
        var (vm, _) = await OpenAsync(600, 400);
        var document = vm.ActiveDocument!;

        document.Selection = new PixelRect(120, 80, 64, 48);

        // 末尾は、開いたときの M09
        Assert.Equal("画像: 600×400 ／ 選択範囲: (120, 80) 64×48 ／ " + Messages.M09_Opened(1, 0), vm.StatusText);
        document.Selection = null;
        Assert.Equal("画像: 600×400 ／ " + Messages.M09_Opened(1, 0), vm.StatusText);
    }

    [Fact]
    public async Task MainViewModel_NotifiesStatusTextWhenSelectionChanges()
    {
        var (vm, _) = await OpenAsync(600, 400);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ActiveDocument!.Selection = new PixelRect(0, 0, 10, 10);

        Assert.Contains(nameof(MainViewModel.StatusText), changed);
    }

    [Theory]
    [InlineData("free", 0, 0, null, null)]
    [InlineData("original", 0, 0, 3, 2)]   // 600×400 の画像なら 3:2
    [InlineData("fixed", 16, 9, 16, 9)]
    public async Task MainViewModel_SelectionAspectComesFromSettings(string kind, int width, int height, int? ratioWidth, int? ratioHeight)
    {
        var settingsPath = _temp.Combine("settings.json");
        File.WriteAllText(settingsPath, $$"""{ "SelectionAspect": { "Kind": "{{kind}}", "Width": {{width}}, "Height": {{height}} } }""");

        var (vm, _) = await OpenAsync(600, 400, settingsPath);

        var expected = ratioWidth is { } w ? new AspectRatio(w, ratioHeight!.Value) : (AspectRatio?)null;
        Assert.Equal(expected, vm.ActiveDocument!.SelectionRules.Aspect);
    }

    private async Task<(MainViewModel Vm, FakeFormat Format)> OpenAsync(int width, int height, string? settingsPath = null)
    {
        var formats = new FormatRegistry();
        formats.AddWicFormats();
        var fake = new FakeFormat("fake", ".fake") { Image = Blank(width, height) };
        formats.Register(fake);
        var vm = new MainViewModel(formats, new SettingsService(formats, settingsPath ?? _temp.Combine("settings.json")),
            new FakeDialogs(), new AppLog(_temp.Combine("app.log")));
        var path = _temp.Combine("image.fake");
        File.WriteAllBytes(path, new byte[1]);
        await vm.LoadAsync(path);
        return (vm, fake);
    }
}
