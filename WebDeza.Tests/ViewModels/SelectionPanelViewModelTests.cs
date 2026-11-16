using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>編集パネルの「選択範囲」の入力欄（設計書 F-04・4.1。実装計画書 8-7）</summary>
public class SelectionPanelViewModelTests
{
    private static DocumentViewModel Create(int width = 400, int height = 300, AspectRatio? aspect = null)
    {
        var image = ImageBuffer.Create(width, height, new byte[width * height * 4]);
        // 編集しないので、ログは書かれない
        return new DocumentViewModel(@"C:\images\photo.png", image, DisplayImage.Create(image), 0, (_, _) => aspect,
            new FakeDialogs(), new AppLog(@"C:\unused\app.log"));
    }

    private static (SelectionPanelViewModel Panel, DocumentViewModel Document) WithSelection(
        PixelRect selection, AspectRatio? aspect = null)
    {
        var document = Create(aspect: aspect);
        document.Selection = selection;
        return (new SelectionPanelViewModel { Document = document }, document);
    }

    private static (string, string, string, string) Texts(SelectionPanelViewModel panel)
        => (panel.XText, panel.YText, panel.WidthText, panel.HeightText);

    // ---------------------------------------------------------------- 表示

    [Fact]
    public void NoDocumentOrNoSelection_EmptyAndDisabled()
    {
        var panel = new SelectionPanelViewModel();
        Assert.False(panel.HasSelection);
        Assert.Equal(("", "", "", ""), Texts(panel));

        panel.Document = Create();
        Assert.False(panel.HasSelection);
        Assert.Equal(("", "", "", ""), Texts(panel));
    }

    [Fact]
    public void ShowsSelection_AndFollowsChanges()
    {
        var (panel, document) = WithSelection(new PixelRect(10, 20, 300, 200));
        Assert.True(panel.HasSelection);
        Assert.Equal(("10", "20", "300", "200"), Texts(panel));

        var changed = new List<string?>();
        panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        document.Selection = new PixelRect(1, 2, 3, 4); // ドラッグ・矢印・メニューで変わったとき

        Assert.Equal(("1", "2", "3", "4"), Texts(panel));
        Assert.Contains(nameof(SelectionPanelViewModel.XText), changed);
        Assert.Contains(nameof(SelectionPanelViewModel.HeightText), changed);
    }

    [Fact]
    public void OtherDocument_ShowsItsSelection_AndStopsFollowingOld()
    {
        var (panel, old) = WithSelection(new PixelRect(10, 20, 30, 40));
        var other = Create();
        other.Selection = new PixelRect(5, 6, 7, 8);

        panel.Document = other;
        Assert.Equal(("5", "6", "7", "8"), Texts(panel));

        int changed = 0;
        panel.PropertyChanged += (_, _) => changed++;
        old.Selection = null;
        Assert.Equal(0, changed);
    }

    // ---------------------------------------------------------------- 入力（画像 400×300、選択範囲 (100, 100) 50×50）

    [Fact]
    public void InputEachField_ChangesSelection()
    {
        var (panel, document) = WithSelection(new PixelRect(100, 100, 50, 50));

        panel.XText = "120";
        panel.YText = "80";
        panel.WidthText = "200";
        panel.HeightText = "150";

        Assert.Equal(new PixelRect(120, 80, 200, 150), document.Selection);
        Assert.Equal(("120", "80", "200", "150"), Texts(panel));
    }

    [Theory]
    [InlineData(nameof(SelectionField.X), "-30", 0, 100, 50, 50)]       // 左にはみ出さない
    [InlineData(nameof(SelectionField.X), "380", 350, 100, 50, 50)]     // 右端に合わせる（大きさは保つ）
    [InlineData(nameof(SelectionField.Y), "999", 100, 250, 50, 50)]
    [InlineData(nameof(SelectionField.Width), "1000", 0, 100, 400, 50)] // 画像の幅まで。はみ出す分は左へずらす
    [InlineData(nameof(SelectionField.Width), "320", 80, 100, 320, 50)]
    [InlineData(nameof(SelectionField.Height), "0", 100, 100, 50, 1)]   // 最小 1 px
    public void Input_StaysInsideImage(string field, string text, int x, int y, int width, int height)
    {
        var (panel, document) = WithSelection(new PixelRect(100, 100, 50, 50));

        Set(panel, Enum.Parse<SelectionField>(field), text);

        Assert.Equal(new PixelRect(x, y, width, height), document.Selection);
        Assert.Equal((x.ToString(), y.ToString(), width.ToString(), height.ToString()), Texts(panel));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void Input_NotInteger_KeepsSelectionAndShowsItAgain(string text)
    {
        var selection = new PixelRect(100, 100, 50, 50);
        var (panel, document) = WithSelection(selection);
        var changed = new List<string?>();
        panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        panel.WidthText = text;

        Assert.Equal(selection, document.Selection);
        Assert.Equal("50", panel.WidthText);
        Assert.Contains(nameof(SelectionPanelViewModel.WidthText), changed); // 入力欄に今の値を出し直す
    }

    [Fact]
    public void Input_FullWidthDigitsAndSpaces_Accepted()
    {
        var (panel, document) = WithSelection(new PixelRect(100, 100, 50, 50));

        panel.XText = " １２０ ";

        Assert.Equal(120, document.Selection!.Value.X);
    }

    [Fact]
    public void Input_WithoutSelection_DoesNothing()
    {
        var document = Create();
        var panel = new SelectionPanelViewModel { Document = document };

        panel.WidthText = "100";

        Assert.Null(document.Selection);
        Assert.Equal("", panel.WidthText);
    }

    [Fact]
    public void FixedAspect_WidthOrHeightSetsTheOther()
    {
        var (panel, document) = WithSelection(new PixelRect(0, 0, 40, 30), new AspectRatio(4, 3));

        panel.WidthText = "200";
        Assert.Equal(new PixelRect(0, 0, 200, 150), document.Selection);

        panel.HeightText = "90";
        Assert.Equal(new PixelRect(0, 0, 120, 90), document.Selection);
    }

    [Fact]
    public void ApplyField_IsPure()
    {
        var rules = CropFrameCalculator.Rules(400, 300, null);

        Assert.Equal(new PixelRect(350, 10, 50, 20),
            SelectionPanelViewModel.ApplyField(new PixelRect(5, 10, 50, 20), SelectionField.X, 9999, rules));
    }

    [Theory]
    [InlineData("12", true, 12)]
    [InlineData(" -3 ", true, -3)]
    [InlineData("１０", true, 10)]
    [InlineData("+7", true, 7)]
    [InlineData("1,000", false, 0)]
    [InlineData(null, false, 0)]
    public void TryParseInput(string? text, bool ok, int value)
    {
        Assert.Equal((ok, value), (SelectionPanelViewModel.TryParseInput(text, out int parsed), parsed));
    }

    private static void Set(SelectionPanelViewModel panel, SelectionField field, string text)
    {
        switch (field)
        {
            case SelectionField.X: panel.XText = text; break;
            case SelectionField.Y: panel.YText = text; break;
            case SelectionField.Width: panel.WidthText = text; break;
            default: panel.HeightText = text; break;
        }
    }
}
