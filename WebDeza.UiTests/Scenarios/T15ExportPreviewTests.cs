using System.Text.RegularExpressions;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T15 保存のプレビュー（設計書 6.4・6.5）。並べて比較と「結果のみ」、左右のズーム・パンの連動、
/// 品質を下げると圧縮後の画素が大きく変わること、ダイアログの最小の大きさ。formats\sample.jpg（1200×900）を使う
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T15ExportPreviewTests(ITestOutputHelper output) : UiTestBase(output)
{
    private static string Sample => UiTestEnvironment.Image("formats", "sample.jpg");

    [UiFact]
    [Trait("No", "T15")]
    public void T15_SideBySide_LowQualityZoomPanSyncAndResultOnly()
    {
        var app = OpenInWebDeza(Sample);
        var dialog = ExportDialog.Open(app);
        dialog.WaitSizeReady();
        var (left, right) = dialog.PreviewPanes();
        string zoom = dialog.ZoomText();
        Check.That("T15 Ctrl+E → 左が「圧縮前」、右が「圧縮後」の並べて比較。倍率は 100%",
            AppDriver.IsSelected(dialog.Element("SideBySideRadio")) && !left.IsEmpty && left.Right < right.Left && zoom == "100%",
            $"左 {left} ／ 右 {right} ／ {zoom}");

        dialog.EnterQuality(5);
        dialog.WaitSizeReady();
        Thread.Sleep(1000);
        string headline = dialog.Headline();
        double lowQuality = Pixels.ShiftedDiff(Pixels.CaptureScreen(left), Pixels.CaptureScreen(right), maxShift: 0);
        SaveShot(Pixels.CaptureScreen(dialog.Window.BoundingRectangle), "T15_quality5");
        Check.That("T15 品質 5 → 圧縮後にブロックノイズ（圧縮前と大きく違う）。見出し「JPEG 品質 5 ／ {サイズ}」",
            lowQuality > 4 && Regex.IsMatch(headline, @"JPEG 品質 5 ／ [\d.]+ (KB|MB|B)$"), $"左右の差 {lowQuality:F1} ／ {headline}");

        // 連動は、PNG にして（左右が同じ絵になる）、どの操作の後も左右が同じかで確かめる
        dialog.SelectFormat("PNG");
        dialog.WaitSizeReady(TimeSpan.FromSeconds(30));
        Thread.Sleep(1000);
        var results = new List<string>();
        bool sync = true;
        void Compare(string what)
        {
            Thread.Sleep(700);
            double diff = dialog.PaneDiff();
            sync &= diff < 2.5;
            results.Add($"{what} {dialog.ZoomText()} 差 {diff:F2}");
        }
        Compare("最初");
        AppDriver.Invoke(dialog.Element("PreviewFitButton"));
        Compare("全体");
        AppDriver.Invoke(dialog.Element("PreviewActualSizeButton"));
        Compare("100%");
        AppDriver.Invoke(dialog.Element("PreviewZoomInButton"));
        Compare("＋");
        AppDriver.Invoke(dialog.Element("PreviewZoomOutButton"));
        AppDriver.Invoke(dialog.Element("PreviewZoomOutButton"));
        Compare("－");
        Pointer.Wheel(Pointer.Center(right), 2);
        Compare("ホイール");
        var start = Pointer.Center(left);
        Pointer.Drag(start, new Point(start.X - 120, start.Y - 80));
        Compare("ドラッグ");
        int zooms = results.Select(r => r.Split(' ')[1]).Distinct().Count();
        Check.That("T15 ホイール・ドラッグ・［全体］［100%］［－］［＋］ → 左右が同じ位置・同じ倍率で動く", sync && zooms >= 4, string.Join(" ／ ", results));

        AppDriver.Select(dialog.Element("ResultOnlyRadio"));
        Thread.Sleep(500);
        var (leftOnly, rightOnly) = dialog.PreviewPanes();
        bool resultOnly = leftOnly.IsEmpty && rightOnly.Width > right.Width * 1.5;
        AppDriver.Select(dialog.Element("SideBySideRadio"));
        Thread.Sleep(500);
        var (leftBack, _) = dialog.PreviewPanes();
        Check.That("T15 「結果のみ」 → 圧縮後だけが大きく出る。「並べて比較」に戻せる",
            resultOnly && !leftBack.IsEmpty, $"結果のみ 右の幅 {rightOnly.Width}（並べて {right.Width}） ／ 戻した後 左 {leftBack}");

        headline = dialog.Headline();
        AppDriver.Invoke(dialog.Element("PreviewFitButton"));
        Thread.Sleep(700);
        double png = dialog.PaneDiff();
        Check.That("T15 PNG → 見出し「PNG ／ {サイズ}」で、圧縮後は圧縮前と同じ見た目",
            Regex.IsMatch(headline, @"PNG ／ [\d.]+ (KB|MB|B)$") && png < 2.5, $"{headline} ／ 差 {png:F2}");
        dialog.Cancel();
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T15")]
    public void T15_MinimumSize_ButtonsWrapAndSettingsScroll()
    {
        var app = OpenInWebDeza(Sample);
        var dialog = ExportDialog.Open(app);
        dialog.WaitSizeReady();
        var normal = dialog.Window.BoundingRectangle;
        dialog.Window.Patterns.Transform.Pattern.Resize(400, 300);
        Thread.Sleep(600);
        var small = dialog.Window.BoundingRectangle;
        var actualSize = dialog.Element("PreviewActualSizeButton").BoundingRectangle;
        var sideBySide = dialog.Element("SideBySideRadio").BoundingRectangle;
        var settings = dialog.Window.FindFirstChild(cf => cf.ByClassName("ScrollViewer")) ?? throw new InvalidOperationException("右の設定が無い");
        bool scrollable = settings.Patterns.Scroll.Pattern.VerticallyScrollable.Value;
        SaveShot(Pixels.CaptureScreen(small), "T15_small");
        // 最小は 820×560 DIP。画面の拡大率が 100% より大きいと、物理ピクセルではもっと大きい
        Check.That("T15 小さくする → 820×560 より小さくならない。ボタンが 2 段に折り返し、右の設定はスクロールできる",
            small.Width >= 819 && small.Height >= 559 && small.Width < normal.Width && actualSize.Top > sideBySide.Bottom - 2 && scrollable,
            $"{normal.Width}×{normal.Height} → {small.Width}×{small.Height} ／ ［100%］の上 {actualSize.Top}・並べて比較の下 {sideBySide.Bottom} ／ スクロール {scrollable}");
        dialog.Cancel();
        Check.AssertAll();
    }
}
