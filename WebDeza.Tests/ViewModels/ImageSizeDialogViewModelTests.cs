using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>画像サイズのダイアログ（設計書 F-05・4.6、V35。実装計画書 9-4）</summary>
public class ImageSizeDialogViewModelTests
{
    [Fact]
    public void Initially_ShowsCurrentSize_AndKeepsAspect()
    {
        var vm = new ImageSizeDialogViewModel(6000, 4000);

        Assert.Equal("6000 × 4000 px", vm.CurrentSizeText);
        Assert.Equal(("6000", "4000"), (vm.WidthText, vm.HeightText));
        Assert.True(vm.KeepAspect); // 既定はオン
        Assert.True(vm.CanAccept);
        Assert.Equal((6000, 4000), vm.NewSize);
        Assert.Equal("6000 × 4000 px（100%）", vm.ResultText);
    }

    [Fact]
    public void KeepAspect_HalfWidth_HalvesHeight()
    {
        // V35: 縦横比を保って幅を半分に → 高さも半分
        var vm = new ImageSizeDialogViewModel(6000, 4000);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.WidthText = "3000";

        Assert.Equal("2000", vm.HeightText);
        Assert.Equal((3000, 2000), vm.NewSize);
        Assert.Equal("3000 × 2000 px（50%）", vm.ResultText);
        Assert.Contains(nameof(ImageSizeDialogViewModel.HeightText), changed);
        Assert.Contains(nameof(ImageSizeDialogViewModel.ResultText), changed);
        Assert.Contains(nameof(ImageSizeDialogViewModel.CanAccept), changed);
    }

    [Fact]
    public void KeepAspect_Height_DecidesWidth()
    {
        var vm = new ImageSizeDialogViewModel(6000, 4000);

        vm.HeightText = "1280";

        Assert.Equal("1920", vm.WidthText);
        Assert.Equal("1920 × 1280 px（32%）", vm.ResultText);
    }

    [Theory]
    [InlineData(1000, 333, "500", "167")] // 166.5 は四捨五入して 167
    [InlineData(1000, 1, "1", "1")]       // 0.001 になっても最低 1
    [InlineData(3, 2, "30000", "20000")]
    public void KeepAspect_RoundsOtherSide(int width, int height, string widthText, string expectedHeight)
    {
        var vm = new ImageSizeDialogViewModel(width, height);

        vm.WidthText = widthText;

        Assert.Equal(expectedHeight, vm.HeightText);
    }

    [Fact]
    public void WithoutKeepAspect_OtherSideStays()
    {
        // V35: 縦横比を保たない → 高さは変わらない
        var vm = new ImageSizeDialogViewModel(6000, 4000) { KeepAspect = false };

        vm.WidthText = "3000";

        Assert.Equal("4000", vm.HeightText);
        Assert.Equal((3000, 4000), vm.NewSize);
        Assert.Equal("3000 × 4000 px（幅 50%・高さ 100%）", vm.ResultText);
    }

    [Fact]
    public void TurningKeepAspectOn_DecidesHeightFromWidth()
    {
        var vm = new ImageSizeDialogViewModel(6000, 4000) { KeepAspect = false };
        vm.WidthText = "3000";
        vm.HeightText = "100";

        vm.KeepAspect = true;

        Assert.Equal(("3000", "2000"), (vm.WidthText, vm.HeightText));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("30001")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12.5")]
    public void OutOfRange_DisablesOk(string text)
    {
        // V35: 範囲外 → OK が無効
        var vm = new ImageSizeDialogViewModel(6000, 4000);

        vm.WidthText = text;

        Assert.False(vm.CanAccept);
        Assert.Null(vm.NewSize);
        Assert.Equal(ImageSizeDialogViewModel.OutOfRangeText, vm.ResultText);
        Assert.Equal("4000", vm.HeightText); // 正しくない値では、もう片方を変えない
        Assert.Equal(text, vm.WidthText);   // 打っている欄の文字は書き換えない
    }

    [Fact]
    public void TooLargeForArray_DisablesOk()
    {
        // 幅 × 高さ × 4 が配列の上限（約 2 GB）を超える
        var vm = new ImageSizeDialogViewModel(100, 100) { KeepAspect = false };

        vm.WidthText = "30000";
        vm.HeightText = "30000";

        Assert.False(vm.CanAccept);
        Assert.Equal(ImageSizeDialogViewModel.TooLargeText, vm.ResultText);

        vm.HeightText = "10000";
        Assert.True(vm.CanAccept);
    }

    [Fact]
    public void FullWidthDigits_AreAccepted()
    {
        var vm = new ImageSizeDialogViewModel(6000, 4000);

        vm.WidthText = "１５００";

        Assert.Equal("１５００", vm.WidthText);
        Assert.Equal("1000", vm.HeightText);
        Assert.Equal((1500, 1000), vm.NewSize);
    }

    [Fact]
    public void SmallPercent_ShowsOneDecimal()
    {
        var vm = new ImageSizeDialogViewModel(6000, 4000);

        vm.WidthText = "300";

        Assert.Equal("300 × 200 px（5%）", vm.ResultText);
        vm.WidthText = "330";
        Assert.Equal("330 × 220 px（5.5%）", vm.ResultText);
    }
}
