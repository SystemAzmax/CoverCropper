using System.Windows.Media;
using WebDeza.Imaging.Parameters;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>パラメーターの入力欄（設計書 5.6、V22。実装計画書 9-5）</summary>
public class ParameterViewModelTests
{
    private static readonly ParameterDescriptor Quality =
        new("quality", "品質", ParameterKind.Integer, 85, Min: 0, Max: 100);

    private static readonly ParameterDescriptor Radius =
        new("radius", "半径", ParameterKind.Number, 0.0, Min: 0, Max: 100, Step: 0.5, Unit: "px");

    private static readonly ParameterDescriptor Interlace =
        new("interlace", "インターレース", ParameterKind.Boolean, false);

    private static readonly ParameterDescriptor Compression =
        new("compression", "圧縮", ParameterKind.Choice, "lzw",
            Choices: [new ChoiceItem("none", "なし"), new ChoiceItem("lzw", "LZW"), new ChoiceItem("zip", "ZIP")]);

    private static readonly ParameterDescriptor Background =
        new("background", "背景色", ParameterKind.Color, "#FFFFFF");

    // ---------------------------------------------------------------- 作る

    [Fact]
    public void Create_MakesViewModelOfKind()
    {
        Assert.IsType<NumericParameterViewModel>(ParameterViewModel.Create(Quality));
        Assert.IsType<NumericParameterViewModel>(ParameterViewModel.Create(Radius));
        Assert.IsType<BooleanParameterViewModel>(ParameterViewModel.Create(Interlace));
        Assert.IsType<ChoiceParameterViewModel>(ParameterViewModel.Create(Compression));
        Assert.IsType<ColorParameterViewModel>(ParameterViewModel.Create(Background));
    }

    [Fact]
    public void Create_CoercesInitialValue()
    {
        var p = ParameterViewModel.Create(Quality, 250);

        Assert.Equal(100L, p.Value); // 範囲外は丸める
        Assert.Equal(85L, ParameterViewModel.Create(Quality).Value); // 無ければ既定値
        Assert.Equal(85L, ParameterViewModel.Create(Quality, "abc").Value); // 種類に合わなければ既定値
        Assert.Equal("品質", p.DisplayName);
        Assert.Equal("quality", p.Key);
        Assert.Equal("", p.Unit);
        Assert.Equal("px", ParameterViewModel.Create(Radius).Unit);
    }

    [Fact]
    public void CreateAll_AndToValues_RoundTrip()
    {
        var descriptors = new[] { Quality, Interlace, Compression };
        var values = ParameterValues.Defaults(descriptors).With("quality", 60L);

        var parameters = ParameterViewModel.CreateAll(descriptors, values);
        ((BooleanParameterViewModel)parameters[1]).IsChecked = true;

        Assert.Equal(["quality", "interlace", "compression"], parameters.Select(p => p.Key));
        Assert.Equal(values.With("interlace", true), ParameterViewModel.ToValues(parameters));
        Assert.Equal(ParameterValues.Defaults(descriptors), ParameterViewModel.ToValues(ParameterViewModel.CreateAll(descriptors)));
    }

    // ---------------------------------------------------------------- Integer / Number（V22）

    [Fact]
    public void Integer_OverMax_BecomesMax_AndResetReturnsDefault()
    {
        // V22: Integer の記述（0〜100）に 150 を入れる → 100 / 既定値に戻す → 既定値
        var p = (NumericParameterViewModel)ParameterViewModel.Create(Quality);

        p.Text = "150";
        Assert.Equal(100L, p.Value);
        Assert.Equal("100", p.Text);
        Assert.False(p.IsDefault);

        p.ResetToDefault();
        Assert.Equal(85L, p.Value);
        Assert.Equal("85", p.Text);
        Assert.True(p.IsDefault);
    }

    [Fact]
    public void Integer_SliderRoundsToInteger()
    {
        var p = (NumericParameterViewModel)ParameterViewModel.Create(Quality);

        p.SliderValue = 33.6;

        Assert.Equal(34L, p.Value);
        Assert.Equal(34, p.SliderValue);
        Assert.Equal("34", p.Text);
        Assert.Equal((0, 100, 1, 10), (p.Minimum, p.Maximum, p.Step, p.LargeStep));
        Assert.True(p.IsInteger);
    }

    [Fact]
    public void Numeric_TextInput_AcceptsFullWidthAndRejectsGarbage()
    {
        var p = (NumericParameterViewModel)ParameterViewModel.Create(Quality);
        var changed = new List<string?>();
        p.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        p.Text = "　５０ ";
        Assert.Equal(50L, p.Value);

        changed.Clear();
        p.Text = "abc";
        Assert.Equal(50L, p.Value); // 読めない値は受け付けない
        Assert.Equal("50", p.Text);
        Assert.Contains(nameof(NumericParameterViewModel.Text), changed); // 入力欄に今の値を出し直す

        p.Text = "Infinity";
        Assert.Equal(50L, p.Value);
        p.Text = "-20";
        Assert.Equal(0L, p.Value); // 範囲外は丸める
        p.Text = "12.6";
        Assert.Equal(13L, p.Value); // 整数は四捨五入
    }

    [Fact]
    public void Number_KeepsFractions_AndFormatsWithStepDecimals()
    {
        var p = (NumericParameterViewModel)ParameterViewModel.Create(Radius);

        Assert.Equal("0.0", p.Text);
        p.Text = "8";
        Assert.Equal(8.0, p.Value);
        Assert.Equal("8.0", p.Text); // 刻み 0.5 なので小数 1 桁は出す
        p.Text = "3.33";
        Assert.Equal(3.33, p.Value);
        Assert.Equal("3.33", p.Text);
        p.SliderValue = 12.5;
        Assert.Equal(12.5, p.Value);
        p.Text = "1000";
        Assert.Equal(100.0, p.Value);
        Assert.False(p.IsInteger);
        Assert.Equal((0.5, 10.0), (p.Step, p.LargeStep));
    }

    [Theory]
    [InlineData(1, "0.###")]
    [InlineData(0.5, "0.0###")]
    [InlineData(0.25, "0.00###")]
    [InlineData(0, "0.###")]
    public void NumberFormat_FollowsStep(double step, string expected)
        => Assert.Equal(expected, NumericParameterViewModel.NumberFormat(step));

    [Fact]
    public void ValueChanged_RaisedOnlyWhenValueChanges()
    {
        var p = (NumericParameterViewModel)ParameterViewModel.Create(Quality);
        int raised = 0;
        p.ValueChanged += (_, _) => raised++;

        p.SliderValue = 50;
        p.SliderValue = 50;
        p.Text = "50";
        p.Text = "50.2"; // 丸めると同じ値

        Assert.Equal(1, raised);
        p.ResetToDefault();
        Assert.Equal(2, raised);
        p.ResetToDefaultCommand.Execute(null); // 既定値のまま
        Assert.Equal(2, raised);
    }

    // ---------------------------------------------------------------- Boolean・Choice・Color

    [Fact]
    public void Boolean_CheckBoxSetsValue()
    {
        var p = (BooleanParameterViewModel)ParameterViewModel.Create(Interlace);

        p.IsChecked = true;
        Assert.Equal(true, p.Value);
        Assert.False(p.IsDefault);

        p.ResetToDefault();
        Assert.False(p.IsChecked);
        Assert.True(p.IsDefault);
    }

    [Fact]
    public void Choice_SelectsFromChoices()
    {
        var p = (ChoiceParameterViewModel)ParameterViewModel.Create(Compression);
        Assert.Equal("LZW", p.SelectedChoice!.DisplayName);
        Assert.Equal(3, p.Choices.Count);

        p.SelectedChoice = p.Choices[2];
        Assert.Equal("zip", p.Value);

        p.SelectedChoice = new ChoiceItem("other", "ほか"); // 選択肢に無いものは受け付けない
        Assert.Equal("zip", p.Value);
        p.SelectedChoice = null;
        Assert.Equal("zip", p.Value);

        p.ResetToDefault();
        Assert.Equal("lzw", p.Value);
    }

    [Fact]
    public void Color_TextAndSwatches()
    {
        var p = (ColorParameterViewModel)ParameterViewModel.Create(Background);
        Assert.Equal("#FFFFFF", p.Text);
        Assert.Equal(Colors.White, p.Color);

        p.Text = "#ff8000";
        Assert.Equal("#FF8000", p.Value);
        Assert.Equal("#FF8000", p.Text);
        Assert.Equal(Color.FromRgb(0xFF, 0x80, 0x00), p.Color);

        p.Text = "00ff00"; // 「#」は省いてよい
        Assert.Equal("#00FF00", p.Value);
        p.Text = "＃０００ＦＦ０"; // 全角も受け付ける
        Assert.Equal("#000FF0", p.Value);

        p.Text = "#12345"; // 読めない色は受け付けない（既定値にもしない）
        Assert.Equal("#000FF0", p.Value);
        Assert.Equal("#000FF0", p.Text);
        p.Text = "red";
        Assert.Equal("#000FF0", p.Value);

        p.SelectSwatchCommand.Execute("#808080");
        Assert.Equal("#808080", p.Value);
        Assert.Equal(["#FFFFFF", "#000000", "#808080"], ColorParameterViewModel.Swatches);

        p.ResetToDefault();
        Assert.True(p.IsDefault);
    }
}
