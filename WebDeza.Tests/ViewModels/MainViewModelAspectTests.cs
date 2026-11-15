using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Models;
using WebDeza.Services;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>選択範囲の比率（設計書 F-04。V43。実装計画書 8-8）</summary>
public sealed class MainViewModelAspectTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeFormat _fake = new("fake", ".fake");
    private readonly FormatRegistry _formats = new();

    public MainViewModelAspectTests()
    {
        _formats.AddWicFormats();
        _formats.Register(_fake);
        _fake.Image = ImageBuffer.Create(120, 80, new byte[120 * 80 * 4]);
    }

    public void Dispose() => _temp.Dispose();

    private string SettingsPath => _temp.Combine("settings.json");

    private MainViewModel CreateVm()
        => new(_formats, new SettingsService(_formats, SettingsPath), new FakeDialogs(), new AppLog(_temp.Combine("logs", "app.log")));

    /// <summary>保存された設定を読み直したもの</summary>
    private SelectionAspectData SavedAspect() => new SettingsService(_formats, SettingsPath).Load().Settings.SelectionAspect;

    private string CreateFile(string name)
    {
        var path = _temp.Combine(name);
        File.WriteAllBytes(path, new byte[16]);
        return path;
    }

    private static AspectOptionViewModel Option(MainViewModel vm, string label)
        => vm.AspectOptions.Single(o => o.Label == label);

    // ---------------------------------------------------------------- 選択欄

    [Fact]
    public void Options_InDesignOrder_FreeByDefault()
    {
        var vm = CreateVm();

        Assert.Equal(new[] { "自由", "元画像と同じ", "1:1", "4:3", "3:2", "16:9", "カスタム" }, vm.AspectOptions.Select(o => o.Label));
        Assert.Same(vm.AspectOptions[0], vm.SelectedAspect);
        Assert.False(vm.IsCustomAspect);
        Assert.False(vm.SwapAspectCommand.CanExecute(null)); // 自由は入れ替えられない
    }

    // ---------------------------------------------------------------- V43

    [Fact]
    public async Task ChooseSquare_SelectionBecomesSquare_AndSaved()
    {
        var vm = CreateVm();
        await vm.LoadAsync(CreateFile("a.fake"));
        var document = vm.ActiveDocument!;
        document.Selection = new PixelRect(10, 10, 60, 40); // 面積 2400、中心 (40, 30)

        vm.SelectedAspect = Option(vm, "1:1");

        // 中心と面積をなるべく保って正方形に（49×49）
        Assert.Equal(new PixelRect(15, 5, 49, 49), document.Selection);
        var saved = SavedAspect();
        Assert.Equal((SelectionAspectData.FixedKind, 1, 1), (saved.Kind, saved.Width, saved.Height));
    }

    [Fact]
    public async Task Swap_4x3Becomes3x4_AndBack()
    {
        var vm = CreateVm();
        await vm.LoadAsync(CreateFile("a.fake"));
        var document = vm.ActiveDocument!;
        document.Selection = new PixelRect(20, 20, 40, 30);
        vm.SelectedAspect = Option(vm, "4:3");
        Assert.Equal(new PixelRect(20, 20, 40, 30), document.Selection); // もう 4:3 なので変わらない
        Assert.True(vm.SwapAspectCommand.CanExecute(null));

        vm.SwapAspectCommand.Execute(null);

        Assert.Equal("3:4", vm.SelectedAspect.Label);
        Assert.Equal(new[] { "1:1", "3:4", "2:3", "9:16" },
            vm.AspectOptions.Where(o => o.Choice == AspectChoice.Preset).Select(o => o.Label));
        Assert.Equal(new PixelRect(25, 15, 30, 40), document.Selection);
        Assert.Equal((3, 4), (SavedAspect().Width, SavedAspect().Height));

        vm.SwapAspectCommand.Execute(null);

        Assert.Equal("4:3", vm.SelectedAspect.Label);
        Assert.Equal(new PixelRect(20, 20, 40, 30), document.Selection); // 戻せば元どおり（縮まない）
    }

    [Fact]
    public void Swap_KeepsPortraitWhenChoosingOtherPreset()
    {
        var vm = CreateVm();
        vm.SelectedAspect = Option(vm, "4:3");
        vm.SwapAspectCommand.Execute(null);

        vm.SelectedAspect = Option(vm, "9:16");

        Assert.Equal((9, 16), (vm.CurrentSelectionAspect.Width, vm.CurrentSelectionAspect.Height));
    }

    [Fact]
    public void Swap_DisabledForSquareAndOriginal()
    {
        var vm = CreateVm();

        vm.SelectedAspect = Option(vm, "1:1");
        Assert.False(vm.SwapAspectCommand.CanExecute(null));
        vm.SelectedAspect = Option(vm, "元画像と同じ");
        Assert.False(vm.SwapAspectCommand.CanExecute(null));
        Assert.Equal(SelectionAspectData.OriginalKind, SavedAspect().Kind);
    }

    [Fact]
    public async Task Original_FitsImageRatio_AndFreeKeepsSelection()
    {
        var vm = CreateVm();
        await vm.LoadAsync(CreateFile("a.fake")); // 120×80（3:2）
        var document = vm.ActiveDocument!;
        document.Selection = new PixelRect(30, 20, 40, 40);

        vm.SelectedAspect = Option(vm, "元画像と同じ");
        var fitted = document.Selection!.Value;
        Assert.Equal(new AspectRatio(3, 2), document.SelectionRules.Aspect);
        Assert.Equal(document.SelectionRules.HeightForWidth(fitted.Width), fitted.Height); // 高さは比率で幅から求めたもの

        vm.SelectedAspect = Option(vm, "自由");
        Assert.Equal(fitted, document.Selection);
        Assert.Equal(SelectionAspectData.FreeKind, SavedAspect().Kind);
    }

    [Fact]
    public async Task ChangingAspect_RefitsAllOpenDocuments()
    {
        var vm = CreateVm();
        await vm.OpenPathsAsync([CreateFile("a.fake"), CreateFile("b.fake")]);
        vm.Documents[0].Selection = new PixelRect(10, 10, 60, 40);
        vm.Documents[1].Selection = new PixelRect(0, 0, 30, 10);

        vm.SelectedAspect = Option(vm, "1:1");

        Assert.All(vm.Documents, d => Assert.Equal(d.Selection!.Value.Width, d.Selection!.Value.Height));
    }

    // ---------------------------------------------------------------- カスタム

    [Fact]
    public void Custom_StartsFromPreviousPreset_AndInputChangesRatio()
    {
        var vm = CreateVm();
        vm.SelectedAspect = Option(vm, "16:9");

        vm.SelectedAspect = Option(vm, "カスタム");
        Assert.True(vm.IsCustomAspect);
        Assert.Equal(("16", "9"), (vm.CustomAspectWidthText, vm.CustomAspectHeightText));

        vm.CustomAspectWidthText = "1200";
        vm.CustomAspectHeightText = "630";
        var saved = SavedAspect();
        Assert.Equal((SelectionAspectData.FixedKind, 1200, 630), (saved.Kind, saved.Width, saved.Height));
    }

    [Fact]
    public async Task Custom_TypingWidthThenHeight_DoesNotShrinkSelection()
    {
        // 幅を打った途中の比率（1200:9 など）で細くなっても、次の比率は、比率を変え始める前の選択範囲から合わせ直す
        _fake.Image = ImageBuffer.Create(3000, 1000, new byte[3000 * 1000 * 4]);
        var vm = CreateVm();
        await vm.LoadAsync(CreateFile("a.fake"));
        var document = vm.ActiveDocument!;
        document.Selection = new PixelRect(600, 200, 1200, 600);
        vm.SelectedAspect = Option(vm, "16:9");
        vm.SelectedAspect = Option(vm, "カスタム");

        vm.CustomAspectWidthText = "1200";
        vm.CustomAspectHeightText = "630";

        var rules = document.SelectionRules;
        Assert.Equal(CropFrameCalculator.FitToAspect(new PixelRect(600, 200, 1200, 600), rules), document.Selection);
    }

    [Fact]
    public async Task Refit_StartsFromSelectionChangedByUser()
    {
        // 比率の合わせ直しのほか（ドラッグ・入力など）で選択範囲を変えたら、それが次の合わせ直しの元になる
        var vm = CreateVm();
        await vm.LoadAsync(CreateFile("a.fake"));
        var document = vm.ActiveDocument!;
        document.Selection = new PixelRect(10, 10, 60, 40);
        vm.SelectedAspect = Option(vm, "1:1");
        document.Selection = new PixelRect(0, 0, 20, 20);

        vm.SelectedAspect = Option(vm, "4:3");

        Assert.Equal(CropFrameCalculator.FitToAspect(new PixelRect(0, 0, 20, 20), document.SelectionRules), document.Selection);
        Assert.Equal(new PixelRect(0, 1, 23, 17), document.Selection);
    }

    [Fact]
    public void Custom_OutOfRangeIsClamped_AndNonIntegerReverts()
    {
        var vm = CreateVm();
        vm.SelectedAspect = Option(vm, "カスタム");

        vm.CustomAspectWidthText = "20000";
        Assert.Equal("10000", vm.CustomAspectWidthText);
        vm.CustomAspectHeightText = "0";
        Assert.Equal("1", vm.CustomAspectHeightText);
        vm.CustomAspectHeightText = "abc";
        Assert.Equal("1", vm.CustomAspectHeightText);
    }

    [Fact]
    public async Task Custom_ChangesSelection_AndSwapExchangesValues()
    {
        var vm = CreateVm();
        await vm.LoadAsync(CreateFile("a.fake"));
        var document = vm.ActiveDocument!;
        document.Selection = new PixelRect(20, 20, 40, 30);
        vm.SelectedAspect = Option(vm, "カスタム"); // 1:1 から

        vm.CustomAspectWidthText = "2";
        Assert.Equal(new AspectRatio(2, 1), document.SelectionRules.Aspect);
        Assert.Equal((49, 25), (document.Selection!.Value.Width, document.Selection!.Value.Height)); // 2:1（高さは四捨五入）

        vm.SwapAspectCommand.Execute(null);

        Assert.Equal(("1", "2"), (vm.CustomAspectWidthText, vm.CustomAspectHeightText));
        Assert.Equal(new AspectRatio(1, 2), document.SelectionRules.Aspect);
        Assert.True(document.Selection!.Value.Height > document.Selection!.Value.Width);
    }

    // ---------------------------------------------------------------- 設定から

    [Theory]
    [InlineData(4, 3, "4:3", false)]
    [InlineData(3, 4, "3:4", false)]   // 縦長の 4:3
    [InlineData(1200, 630, "カスタム", true)]
    public void LoadsFromSettings(int width, int height, string label, bool custom)
    {
        var settings = new AppSettings { SelectionAspect = SelectionAspectData.Fixed(width, height) };
        new SettingsService(_formats, SettingsPath).Save(settings);

        var vm = CreateVm();

        Assert.Equal(label, vm.SelectedAspect.Label);
        Assert.Equal(custom, vm.IsCustomAspect);
        Assert.Equal((width, height), (vm.CurrentSelectionAspect.Width, vm.CurrentSelectionAspect.Height));
    }

    [Fact]
    public void LoadsOriginalFromSettings()
    {
        new SettingsService(_formats, SettingsPath).Save(new AppSettings { SelectionAspect = SelectionAspectData.Original() });

        Assert.Equal("元画像と同じ", CreateVm().SelectedAspect.Label);
    }
}
