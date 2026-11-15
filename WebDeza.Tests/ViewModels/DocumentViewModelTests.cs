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

    public void Dispose() => _temp.Dispose();

    private static ImageBuffer Blank(int width, int height) => ImageBuffer.Create(width, height, new byte[width * height * 4]);

    private static DocumentViewModel Create(int width = 600, int height = 400, AspectRatio? aspect = null)
    {
        var image = Blank(width, height);
        return new DocumentViewModel(@"C:\images\photo.png", image, DisplayImage.Create(image), 1234, (_, _) => aspect);
    }

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
    }

    [Fact]
    public void Constructor_DisplayOfOtherSize_Throws()
    {
        var image = Blank(10, 10);

        Assert.Throws<ArgumentException>(() =>
            new DocumentViewModel("a.png", image, DisplayImage.Create(Blank(5, 5)), 0, (_, _) => null));
    }

    // ---------------------------------------------------------------- 選択範囲

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
