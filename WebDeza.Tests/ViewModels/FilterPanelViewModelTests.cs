using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Services;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>編集パネルの「フィルター」: フィルターの選択とプレビュー（設計書 F-06・5.8、V36。実装計画書 9-6）</summary>
public sealed class FilterPanelViewModelTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeFilter _filter = new();
    private readonly FakeFilter _other = new("other");
    private readonly FilterRegistry _filters = new();

    public FilterPanelViewModelTests()
    {
        _filters.Register(_filter);
        _filters.Register(_other);
    }

    public void Dispose() => _temp.Dispose();

    private string LogFile => _temp.Combine("logs", "app.log");

    private static ImageBuffer Blank(int width, int height) => ImageBuffer.Create(width, height, new byte[width * height * 4]);

    private DocumentViewModel CreateDocument(int width = 60, int height = 40)
    {
        var image = Blank(width, height);
        return new DocumentViewModel(@"C:\images\photo.png", image, DisplayImage.Create(image), 0, (_, _) => null,
            _dialogs, new AppLog(LogFile));
    }

    private FilterPanelViewModel CreatePanel(DocumentViewModel? document = null)
        => new(_filters, new AppLog(LogFile)) { Document = document };

    private static NumericParameterViewModel Amount(FilterPanelViewModel panel) => (NumericParameterViewModel)panel.Parameters[0];

    /// <summary>(x, y) の画素の青（どのチャンネルも同じ値にするフィルターなので、1 つ見ればよい）</summary>
    private static byte At(DisplayImage display, int x, int y) => display.Image.Pixels[(y * display.Image.Width + x) * 4];

    // ---------------------------------------------------------------- フィルターの一覧と入力欄

    [Fact]
    public void Initially_ListsRegisteredFilters_AndSelectsFirst()
    {
        var panel = CreatePanel();

        Assert.Equal([_filter, _other], panel.Filters); // 登録順
        Assert.Same(_filter, panel.SelectedFilter);
        var parameter = Assert.Single(panel.Parameters);
        Assert.Equal(FakeFilter.AmountKey, parameter.Key);
        Assert.True(parameter.IsDefault);
        Assert.True(panel.IsIdentity);
        Assert.False(panel.ResetCommand.CanExecute(null));
        Assert.Equal("", panel.TargetText); // 画像が無い
        Assert.True(panel.PreviewTask.IsCompleted);
    }

    [Fact]
    public void NoFilters_HasNoParameters()
    {
        var panel = new FilterPanelViewModel(new FilterRegistry(), new AppLog(LogFile)) { Document = CreateDocument() };

        Assert.Null(panel.SelectedFilter);
        Assert.Empty(panel.Parameters);
        Assert.True(panel.IsIdentity);
    }

    [Fact]
    public void TargetText_FollowsSelection()
    {
        var document = CreateDocument();
        var panel = CreatePanel(document);
        var changed = new List<string?>();
        panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("対象: 画像全体", panel.TargetText);
        document.Selection = new PixelRect(5, 5, 10, 10);
        Assert.Equal("対象: 選択範囲", panel.TargetText);
        Assert.Contains(nameof(FilterPanelViewModel.TargetText), changed);
    }

    // ---------------------------------------------------------------- プレビュー（V36）

    [Fact]
    public async Task ChangingValue_ShowsPreview_WithoutChangingImageOrHistory()
    {
        // V36: 値を変える → Display がプレビューになり、Image と履歴は変わらない
        var document = CreateDocument();
        var panel = CreatePanel(document);
        var before = (Image: document.Image, Base: document.BaseDisplay, StateId: document.History.StateId);

        Amount(panel).SliderValue = 77;
        await panel.PreviewTask;

        Assert.True(document.IsPreviewing);
        Assert.NotSame(before.Base, document.Display);
        Assert.Equal(77, At(document.Display, 30, 20));
        Assert.Equal((60, 40), (document.Display.ImageWidth, document.Display.ImageHeight));
        Assert.Same(before.Image, document.Image);
        Assert.Same(before.Base, document.BaseDisplay);
        Assert.Equal(0, At(document.BaseDisplay, 30, 20)); // フィルター前の表示用画像は変えない
        Assert.Equal(before.StateId, document.History.StateId);
        Assert.False(document.CanUndo);
        Assert.False(document.HasUnexportedEdits);
        Assert.False(panel.IsIdentity);
        Assert.True(panel.ResetCommand.CanExecute(null));
        Assert.Equal((60, 40, 77, 1.0), Assert.Single(_filter.Calls)); // 縮小していなければ PixelScale は 1
    }

    [Fact]
    public async Task Reset_ClearsPreview_AndReturnsToDefaults()
    {
        // V36: ［リセット］ → プレビューが消え既定値に戻る
        var document = CreateDocument();
        var panel = CreatePanel(document);
        Amount(panel).SliderValue = 77;
        await panel.PreviewTask;

        panel.ResetCommand.Execute(null);
        await panel.PreviewTask;

        Assert.False(document.IsPreviewing);
        Assert.Same(document.BaseDisplay, document.Display);
        Assert.True(Amount(panel).IsDefault);
        Assert.True(panel.IsIdentity);
        Assert.False(panel.ResetCommand.CanExecute(null));
        Assert.Single(_filter.Calls); // 既定値では、フィルターをかけない
    }

    [Fact]
    public async Task ValueBackToDefault_ClearsPreview()
    {
        var document = CreateDocument();
        var panel = CreatePanel(document);
        Amount(panel).SliderValue = 77;
        await panel.PreviewTask;

        Amount(panel).SliderValue = 0;
        await panel.PreviewTask;

        Assert.False(document.IsPreviewing);
    }

    [Fact]
    public async Task Preview_AppliesOnlyToSelection_AndFollowsSelectionChange()
    {
        var document = CreateDocument();
        document.Selection = new PixelRect(10, 10, 20, 10);
        var panel = CreatePanel(document);

        Amount(panel).SliderValue = 50;
        await panel.PreviewTask;

        Assert.Equal(50, At(document.Display, 10, 10));
        Assert.Equal(50, At(document.Display, 29, 19));
        Assert.Equal(0, At(document.Display, 9, 10));  // 範囲の外は変わらない
        Assert.Equal(0, At(document.Display, 30, 19));
        Assert.Equal((20, 10, 50, 1.0), _filter.Calls[^1]); // 範囲の中の画素だけを材料にする

        // 選択範囲を変えると、新しい範囲でプレビューし直す
        document.Selection = new PixelRect(40, 0, 10, 5);
        await panel.PreviewTask;

        Assert.Equal(0, At(document.Display, 10, 10));
        Assert.Equal(50, At(document.Display, 40, 0));
        Assert.Equal(50, At(document.Display, 49, 4));

        // 選択範囲を解除すると、画像全体にかける
        document.Selection = null;
        await panel.PreviewTask;

        Assert.Equal(50, At(document.Display, 0, 0));
        Assert.Equal("対象: 画像全体", panel.TargetText);
    }

    [Fact]
    public async Task Preview_OnReducedDisplay_ConvertsSelectionAndPixelScale()
    {
        // 表示用画像が 1/2 に縮小される大きさ（長辺 8192 → 4096）
        var document = CreateDocument(8192, 64);
        document.Selection = new PixelRect(1001, 11, 999, 21);
        var panel = CreatePanel(document);

        Amount(panel).SliderValue = 9;
        await panel.PreviewTask;

        // 選択範囲は表示用画像の座標に換算し（外側へ丸める: X 500〜1000、Y 5〜16）、PixelScale は 2
        Assert.Equal((500, 11, 9, 2.0), Assert.Single(_filter.Calls));
        Assert.Equal((4096, 32), (document.Display.Image.Width, document.Display.Image.Height));
        Assert.Equal((8192, 64), (document.Display.ImageWidth, document.Display.ImageHeight));
        Assert.Equal(9, At(document.Display, 500, 5));
        Assert.Equal(9, At(document.Display, 999, 15));
        Assert.Equal(0, At(document.Display, 499, 5));
        Assert.Equal(0, At(document.Display, 1000, 15));
        Assert.Equal(0, At(document.Display, 500, 16));
    }

    [Theory]
    [InlineData(0, 0, 8192, 64, 0, 0, 4096, 32)]
    [InlineData(8191, 63, 1, 1, 4095, 31, 1, 1)] // 端の 1 px も、表示用画像の内側の 1 px になる
    [InlineData(3, 3, 1, 1, 1, 1, 1, 1)]
    [InlineData(2, 2, 3, 3, 1, 1, 2, 2)]         // 外側へ丸める
    public void ToDisplayRect_RoundsOutward_AndStaysInside(int x, int y, int w, int h, int ex, int ey, int ew, int eh)
    {
        var display = DisplayImage.Create(Blank(8192, 64));

        Assert.Equal(2.0, display.PixelScale);
        Assert.Equal(new PixelRect(ex, ey, ew, eh), display.ToDisplayRect(new PixelRect(x, y, w, h)));
    }

    [Fact]
    public async Task ChangesWhileComputing_AreThinned_AndLastValueWins()
    {
        // スライダーの操作中は間引き、止まったら必ず最後の値で更新する
        var document = CreateDocument();
        var panel = CreatePanel(document);
        var gate = new ManualResetEventSlim();
        _filter.OnApply = () => gate.Wait(TimeSpan.FromSeconds(10));

        Amount(panel).SliderValue = 10; // 計算が始まり、止まる
        Amount(panel).SliderValue = 20; // 計算中の変更は、まとめて 1 回にする
        Amount(panel).SliderValue = 30;
        Amount(panel).SliderValue = 40;
        Assert.False(panel.PreviewTask.IsCompleted);
        gate.Set();
        await panel.PreviewTask;

        Assert.Equal([10, 40], _filter.Calls.Select(c => c.Amount));
        Assert.Equal(40, At(document.Display, 0, 0));
    }

    // ---------------------------------------------------------------- プレビューの終わり

    [Fact]
    public async Task SelectingOtherFilter_EndsPreview_AndReplacesParameters()
    {
        var document = CreateDocument();
        var panel = CreatePanel(document);
        var oldParameter = Amount(panel);
        oldParameter.SliderValue = 77;
        await panel.PreviewTask;
        var changed = new List<string?>();
        panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        panel.SelectedFilter = _other;
        await panel.PreviewTask;

        Assert.Same(_other, panel.SelectedFilter);
        Assert.False(document.IsPreviewing);
        Assert.NotSame(oldParameter, Amount(panel));
        Assert.True(Amount(panel).IsDefault);
        Assert.Contains(nameof(FilterPanelViewModel.Parameters), changed);

        // 前のフィルターの入力欄を変えても、もうプレビューしない
        oldParameter.SliderValue = 5;
        await panel.PreviewTask;
        Assert.False(document.IsPreviewing);

        // 一覧に無いもの・null は選べない
        panel.SelectedFilter = new FakeFilter("unknown");
        panel.SelectedFilter = null;
        Assert.Same(_other, panel.SelectedFilter);
    }

    [Fact]
    public async Task SwitchingDocument_ClearsPreview_AndResetsValues()
    {
        // 追加: ほかの画像に切り替えるとプレビューが消える
        var first = CreateDocument();
        var second = CreateDocument();
        var panel = CreatePanel(first);
        Amount(panel).SliderValue = 77;
        await panel.PreviewTask;
        Assert.True(first.IsPreviewing);

        panel.Document = second;
        await panel.PreviewTask;

        Assert.False(first.IsPreviewing);
        Assert.False(second.IsPreviewing);
        Assert.True(Amount(panel).IsDefault);

        // 前の画像の選択範囲を変えても、もう関係しない
        first.Selection = new PixelRect(0, 0, 5, 5);
        Amount(panel).SliderValue = 12;
        await panel.PreviewTask;
        Assert.False(first.IsPreviewing);
        Assert.True(second.IsPreviewing);
    }

    [Fact]
    public async Task SwitchingDocumentWhileComputing_DiscardsTheResult()
    {
        var first = CreateDocument();
        var panel = CreatePanel(first);
        var gate = new ManualResetEventSlim();
        _filter.OnApply = () => gate.Wait(TimeSpan.FromSeconds(10));
        Amount(panel).SliderValue = 77;
        var computing = panel.PreviewTask;

        panel.Document = null; // 計算中に画像が無くなる
        gate.Set();
        await computing;

        Assert.False(first.IsPreviewing); // 古い計算の結果は捨てる
    }

    [Fact]
    public async Task EditingTheImage_EndsPreview_AndResetsValues()
    {
        // その画像を編集する（切り抜き・回転など）と、プレビューをやめる
        var document = CreateDocument();
        var panel = CreatePanel(document);
        Amount(panel).SliderValue = 77;
        await panel.PreviewTask;

        await document.ApplyEditAsync("編集", (_, _) => Blank(30, 20), changedRegion: null, newSelection: null, resetView: true);
        await panel.PreviewTask;

        Assert.False(document.IsPreviewing);
        Assert.Same(document.BaseDisplay, document.Display);
        Assert.Equal((30, 20), (document.Display.ImageWidth, document.Display.ImageHeight));
        Assert.True(Amount(panel).IsDefault);

        // 元に戻したときも同じ
        Amount(panel).SliderValue = 33;
        await panel.PreviewTask;
        Assert.True(document.IsPreviewing);
        await document.UndoAsync();
        await panel.PreviewTask;
        Assert.False(document.IsPreviewing);
        Assert.True(Amount(panel).IsDefault);
        Assert.Equal((60, 40), (document.Display.ImageWidth, document.Display.ImageHeight));
    }

    [Fact]
    public async Task FilterThrows_KeepsDisplay_AndWritesLog()
    {
        // プレビューの計算に失敗しても、表示を変えずにログに書く（ダイアログは出さない。設計書 5.10-6）
        var document = CreateDocument();
        var panel = CreatePanel(document);
        _filter.Throw = () => new InvalidOperationException("壊れた");

        Amount(panel).SliderValue = 77;
        await panel.PreviewTask;

        Assert.False(document.IsPreviewing);
        Assert.Empty(_dialogs.Errors);
        Assert.Contains("Preview failed: fake", File.ReadAllText(LogFile));

        // 直れば、次の値からプレビューできる
        _filter.Throw = null;
        Amount(panel).SliderValue = 78;
        await panel.PreviewTask;
        Assert.True(document.IsPreviewing);
    }

    // ---------------------------------------------------------------- MainViewModel との組み合わせ

    [Fact]
    public async Task MainViewModel_PanelFollowsActiveDocument()
    {
        var formats = new FormatRegistry();
        formats.AddWicFormats();
        formats.Register(new FakeFormat("fake", ".fake"));
        var vm = new MainViewModel(formats, new SettingsService(formats, _temp.Combine("settings.json")),
            _dialogs, new AppLog(LogFile), filters: _filters);
        var a = _temp.Combine("a.fake");
        var b = _temp.Combine("b.fake");
        File.WriteAllBytes(a, new byte[1]);
        File.WriteAllBytes(b, new byte[1]);
        await vm.OpenPathsAsync([a, b]);
        var panel = vm.FilterPanel;
        var active = vm.ActiveDocument!;
        Assert.Same(active, panel.Document);

        Amount(panel).SliderValue = 77;
        await panel.PreviewTask;
        Assert.True(active.IsPreviewing);

        // ほかの画像に切り替えると、プレビューが消える
        vm.ActiveDocument = vm.Documents.First(d => d != active);
        await panel.PreviewTask;
        Assert.False(active.IsPreviewing);
        Assert.Same(vm.ActiveDocument, panel.Document);
        Assert.True(Amount(panel).IsDefault);

        // プレビュー中に切り抜くと、プレビューをやめて切り抜く（切り抜くのは、フィルターをかける前の画像）
        var document = vm.ActiveDocument!;
        Amount(panel).SliderValue = 50;
        await panel.PreviewTask;
        document.Selection = new PixelRect(0, 0, 10, 10);
        await panel.PreviewTask;
        await vm.CropCommand.ExecuteAsync(null);
        await panel.PreviewTask;
        Assert.False(document.IsPreviewing);
        Assert.Equal((10, 10), (document.ImageWidth, document.ImageHeight));
        Assert.All(document.Image.Pixels, p => Assert.Equal(0, p));
        Assert.True(Amount(panel).IsDefault);

        // 閉じると、パネルの対象も無くなる
        vm.CloseAllCommand.Execute(null);
        Assert.Null(panel.Document);
        Assert.Equal("", panel.TargetText);
    }
}
