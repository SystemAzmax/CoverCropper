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
using WebDeza.Views.Canvas;

namespace WebDeza.Tests.ViewModels;

/// <summary>切り抜き枠と IEditorSession（実装計画書 5-6）。書き出し設定は初回起動時の既定（JPEG・1024×768）</summary>
public sealed class MainViewModelCropTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeFormat _fake = new("fake", ".fake");
    private readonly FormatRegistry _formats = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly MainViewModel _vm;

    /// <summary>6000×2000 で 1024×768 のときの初期状態の枠（4:3 の最大を中央に。設計書 4.1 の画面の例と同じ）</summary>
    private static readonly PixelRect InitialFrame = new(1666, 0, 2667, 2000);

    public MainViewModelCropTests()
    {
        _formats.AddWicFormats();
        _formats.Register(_fake);
        _fake.Image = ImageBuffer.Create(6000, 2000, new byte[6000 * 2000 * 4]);
        _vm = CreateViewModel();
    }

    public void Dispose() => _temp.Dispose();

    private MainViewModel CreateViewModel()
        => new(_formats, new FilterRegistry(), new SettingsService(_formats, _temp.Combine("settings.json")), _dialogs,
            new AppLog(_temp.Combine("app.log")));

    private async Task LoadAsync(MainViewModel? vm = null, string name = "a.fake")
    {
        var path = _temp.Combine(name);
        File.WriteAllBytes(path, new byte[10]);
        await (vm ?? _vm).LoadAsync(path);
    }

    private static EditRecipe WithCrop(PixelRect rect) => EditRecipe.Initial with { Crop = rect };

    // ---------------------------------------------------------------- 枠

    [Fact]
    public void NoImage_NoFrame()
    {
        Assert.Null(_vm.CropFrame);
        Assert.Equal((0, 0), (_vm.EditedWidth, _vm.EditedHeight));
        Assert.Throws<InvalidOperationException>(() => _vm.CropRules);
        Assert.Throws<InvalidOperationException>(() => _vm.Preview(WithCrop(new PixelRect(0, 0, 100, 75))));
        Assert.Throws<InvalidOperationException>(() => _vm.Commit(WithCrop(new PixelRect(0, 0, 100, 75))));
    }

    [Fact]
    public async Task NullCrop_IsInitialFrame()
    {
        await LoadAsync();

        Assert.Null(_vm.Recipe.Crop);
        Assert.Equal(InitialFrame, _vm.CropFrame);
        Assert.Equal(InitialFrame, ((IEditorSession)_vm).GetCropFrame());
        Assert.Equal((6000, 2000), (_vm.EditedWidth, _vm.EditedHeight));
        Assert.Equal(new AspectRatio(1024, 768), _vm.CropRules.Aspect);
    }

    [Fact]
    public async Task CropOutsideImage_IsClampedToRules()
    {
        await LoadAsync();

        _vm.Commit(WithCrop(new PixelRect(5000, -10, 2667, 2000)));

        Assert.Equal(new PixelRect(6000 - 2667, 0, 2667, 2000), _vm.CropFrame);
    }

    // ---------------------------------------------------------------- Preview と Commit

    [Fact]
    public async Task Preview_DoesNotAddHistory()
    {
        await LoadAsync();
        var item = _vm.SelectedItem!;
        int changed = 0;
        var properties = new List<string?>();
        _vm.Changed += (_, _) => changed++;
        _vm.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        var moved = WithCrop(InitialFrame with { X = 100 });
        _vm.Preview(moved);

        Assert.Equal(moved, _vm.Recipe);
        Assert.Equal(InitialFrame with { X = 100 }, _vm.CropFrame);
        Assert.Contains("枠: (100, 0) 2667×2000", _vm.StatusText);
        Assert.Equal(EditRecipe.Initial, item.History.Current);
        Assert.False(item.History.CanUndo);
        Assert.Equal(1, changed);
        Assert.Contains(nameof(MainViewModel.StatusText), properties);
        Assert.Contains(nameof(MainViewModel.CropFrame), properties);
    }

    [Fact]
    public async Task Commit_AddsOneStep()
    {
        await LoadAsync();
        var item = _vm.SelectedItem!;

        // ドラッグの途中を何度 Preview しても、確定は 1 段
        _vm.Preview(WithCrop(InitialFrame with { X = 1000 }));
        _vm.Preview(WithCrop(InitialFrame with { X = 500 }));
        _vm.Commit(WithCrop(InitialFrame with { X = 200 }));

        Assert.Equal(InitialFrame with { X = 200 }, item.History.Current.Crop);
        Assert.Equal(item.History.Current, _vm.Recipe);
        Assert.True(item.History.CanUndo);
        Assert.Equal(EditRecipe.Initial, item.History.Undo());
        Assert.False(item.History.CanUndo);
    }

    [Fact]
    public async Task Commit_SameAsCurrent_DoesNotAddStep()
    {
        await LoadAsync();
        var item = _vm.SelectedItem!;

        // 枠を動かさずに離したとき（キャンバスは元のレシピで Commit する）
        _vm.Preview(EditRecipe.Initial);
        _vm.Commit(EditRecipe.Initial);

        Assert.False(item.History.CanUndo);
        Assert.Equal(EditRecipe.Initial, _vm.Recipe);
    }

    [Fact]
    public async Task Commit_WithSameCoalesceKey_ReplacesPreviousStep()
    {
        await LoadAsync();
        var item = _vm.SelectedItem!;

        _vm.Commit(WithCrop(InitialFrame with { X = 1667 }), "move");
        _vm.Commit(WithCrop(InitialFrame with { X = 1668 }), "move");

        item.History.Undo();
        Assert.False(item.History.CanUndo);
    }

    [Fact]
    public async Task LoadingAnotherImage_DiscardsPreview()
    {
        await LoadAsync();
        _vm.Preview(WithCrop(InitialFrame with { X = 0 }));

        await LoadAsync(name: "b.fake");

        Assert.Equal(EditRecipe.Initial, _vm.Recipe);
        Assert.Equal(InitialFrame, _vm.CropFrame);
    }

    [Fact]
    public async Task LoadingAnotherImage_RaisesChanged()
    {
        int changed = 0;
        _vm.Changed += (_, _) => changed++;

        await LoadAsync();

        Assert.True(changed > 0);
    }

    // ---------------------------------------------------------------- ステータスバー

    [Fact]
    public async Task StatusText_ShowsImageFrameAndOutput()
    {
        await LoadAsync();

        // 書き出し倍率 = 1024 ÷ 2667 = 0.38
        Assert.Equal("画像: 6000×2000 ／ 枠: (1666, 0) 2667×2000 ／ 書き出し: 1024×768 JPEG（0.38 倍）", _vm.StatusText);
    }

    [Fact]
    public async Task StatusText_Upscaling_ShowsM03()
    {
        _fake.Image = ImageBuffer.Create(800, 600, new byte[800 * 600 * 4]);
        await LoadAsync();

        // 枠 800×600 を 1024×768 にするので拡大になる
        Assert.Equal($"画像: 800×600 ／ 枠: (0, 0) 800×600 ／ 書き出し: 1024×768 JPEG（1.28 倍） ／ {Messages.M03_Upscaled}",
            _vm.StatusText);
    }

    [Fact]
    public async Task StatusText_SameSize_DoesNotShowM03()
    {
        _fake.Image = ImageBuffer.Create(1024, 768, new byte[1024 * 768 * 4]);
        await LoadAsync();

        Assert.EndsWith("（1.00 倍）", _vm.StatusText);
        Assert.DoesNotContain(Messages.M03_Upscaled, _vm.StatusText);
    }

    [Fact]
    public async Task StatusText_OriginalSize_FrameIsWholeImage()
    {
        // 書き出しサイズが「指定しない」で比率が自由なら、枠は画像全体で、そのままの大きさで書き出す
        var settings = new SettingsService(_formats, _temp.Combine("settings.json"));
        var saved = settings.Load().Settings;
        saved.Export.Size.Mode = OutputSizeData.OriginalMode;
        settings.Save(saved);
        var vm = CreateViewModel();

        await LoadAsync(vm);

        Assert.Equal(new PixelRect(0, 0, 6000, 2000), vm.CropFrame);
        Assert.Equal("画像: 6000×2000 ／ 枠: (0, 0) 6000×2000 ／ 書き出し: 6000×2000 JPEG（1.00 倍）", vm.StatusText);
    }

    [Fact]
    public async Task StatusText_MessageComesLast()
    {
        await LoadAsync();
        _vm.StatusMessage = "お知らせ";

        Assert.EndsWith("（0.38 倍） ／ お知らせ", _vm.StatusText);
    }
}
