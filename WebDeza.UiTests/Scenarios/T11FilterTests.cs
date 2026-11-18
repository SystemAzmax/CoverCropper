using System.Diagnostics;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T11 フィルター（設計書 6.4・6.5）。プレビューと適用、選択範囲だけにかけること、プレビューの終わり、大きな画像。
/// 画素で比べるので、可逆の formats\sample.png（1200×900）を使う
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T11FilterTests(ITestOutputHelper output) : UiTestBase(output)
{
    private static string SamplePng => UiTestEnvironment.Image("formats", "sample.png");

    [UiFact]
    [Trait("No", "T11")]
    public void T11_WholeImage_PreviewApplyBasicCorrectionAndReset()
    {
        var app = OpenInWebDeza(SamplePng);
        var original = app.CaptureCanvas();

        app.Driver.Menu("フィルター", "ぼかし");
        Thread.Sleep(400);
        var section = app.FilterSection();
        bool expanded = section.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Expanded;
        var filterCombo = AppDriver.ById(section, "FilterCombo") ?? throw new InvalidOperationException("フィルターの一覧が無い");
        string filterName = AppDriver.ComboText(filterCombo), target = Target(section);
        Check.That("T11 ［フィルター］→［ぼかし…］ → 編集パネルの「フィルター」が開き、「ぼかし」になる。「対象: 画像全体」",
            expanded && filterName == "ぼかし" && target == "対象: 画像全体", $"開いている {expanded} ／ {filterName} ／ {target}");
        var radius = app.FilterParameter("半径");
        var apply = AppDriver.ById(section, "FilterApplyButton") ?? throw new InvalidOperationException("［適用］が無い");
        Check.That("T11 値が既定値（半径 0）のとき → ［適用］が無効", WebDezaApp.SliderValue(radius) == 0 && !apply.IsEnabled);

        var previews = new List<Pixels>();
        foreach (var value in new[] { 3.0, 6, 10 })
        {
            WebDezaApp.SetSlider(radius, value);
            previews.Add(app.SettleAndCapture(900));
        }
        double d1 = Pixels.PixelDiff(original, previews[0]), d2 = Pixels.PixelDiff(previews[0], previews[1]), d3 = Pixels.PixelDiff(previews[1], previews[2]);
        SaveShot(previews[2], "T11_preview_radius10");
        Check.That("T11 半径のスライダーを動かす → プレビューが追いかけて変わる。画像はまだ変わらない（* が付かない）",
            d1 > 0.5 && d2 > 0.3 && d3 > 0.3 && !app.IsDirty(), $"差 {d1:F2} / {d2:F2} / {d3:F2} ／ {app.ActiveDocumentTitle()}");

        AppDriver.Invoke(apply);
        Waits.Until(app.IsDirty, TimeSpan.FromSeconds(10));
        var applied = app.SettleAndCapture();
        double toPreview = Pixels.PixelDiff(applied, previews[2]), toOriginal = Pixels.PixelDiff(applied, original);
        Check.That("T11 ［適用］ → 画像にぼかしがかかり、* が付き、半径が 0 に戻る（見た目はプレビューと同じ）",
            app.IsDirty() && WebDezaApp.SliderValue(radius) == 0 && toPreview < 0.5 && toOriginal > 0.3,
            $"{app.ActiveDocumentTitle()} ／ 半径 {WebDezaApp.SliderValue(radius)} ／ プレビューとの差 {toPreview:F2} ／ 元との差 {toOriginal:F2}");

        app.Driver.SelectCombo(filterCombo, "基本補正");
        Thread.Sleep(400);
        var brightness = app.FilterParameter("明るさ");
        var contrast = app.FilterParameter("コントラスト");
        var saturation = app.FilterParameter("彩度");
        WebDezaApp.SetSlider(brightness, 30);
        var withBrightness = app.SettleAndCapture();
        WebDezaApp.SetSlider(contrast, 30);
        var withContrast = app.SettleAndCapture();
        WebDezaApp.SetSlider(saturation, -100);
        var gray = app.SettleAndCapture();
        SaveShot(gray, "T11_saturation_minus100");
        Check.That("T11 「基本補正」で明るさ・コントラスト・彩度を変える → プレビューが変わる。彩度 −100 で白黒になる",
            Pixels.PixelDiff(applied, withBrightness) > 1 && Pixels.PixelDiff(withBrightness, withContrast) > 1 && gray.Chroma() < 3 && withContrast.Chroma() > 20,
            $"差 {Pixels.PixelDiff(applied, withBrightness):F1} / {Pixels.PixelDiff(withBrightness, withContrast):F1} ／ 彩度 前 {withContrast.Chroma():F1} 後 {gray.Chroma():F1}");

        var thumb = AppDriver.ById(WebDezaApp.ParameterSlider(saturation), "Thumb") ?? throw new InvalidOperationException("スライダーのつまみが無い");
        Pointer.DoubleClick(Pointer.Center(thumb.BoundingRectangle));
        Thread.Sleep(400);
        bool onlySaturation = WebDezaApp.SliderValue(saturation) == 0 && WebDezaApp.SliderValue(brightness) == 30 && WebDezaApp.SliderValue(contrast) == 30;
        AppDriver.Invoke(AppDriver.ById(section, "FilterResetButton") ?? throw new InvalidOperationException("［リセット］が無い"));
        var afterReset = app.SettleAndCapture();
        double resetDiff = Pixels.PixelDiff(afterReset, applied);
        Check.That("T11 スライダーをダブルクリック → その値だけ既定値に戻る。［リセット］ → すべて既定値に戻り、プレビューが消える",
            onlySaturation && WebDezaApp.SliderValue(brightness) == 0 && WebDezaApp.SliderValue(contrast) == 0 && resetDiff < 0.3,
            $"ダブルクリックで彩度だけ戻る {onlySaturation} ／ リセット後の、適用後の絵との差 {resetDiff:F2}");
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T11")]
    public void T11_Selection_PreviewFollowsRangeAndApplyChangesOnlyInside()
    {
        var app = OpenInWebDeza(SamplePng);
        app.Driver.Menu("フィルター", "ぼかし");
        Thread.Sleep(400);
        var section = app.FilterSection();

        app.SetSelection(300, 200, 400, 300);
        WebDezaApp.SetSlider(app.FilterParameter("半径"), 15);
        var first = app.SettleAndCapture(1000);
        string target = Target(section);
        Check.That("T11 選択範囲を作ってから、ぼかしの半径を大きくする → 「対象: 選択範囲」", target == "対象: 選択範囲", target);

        app.Driver.EnterValue(app.Input("SelectionXInput"), "500");
        var moved = app.SettleAndCapture(1000);
        double movedDiff = Pixels.PixelDiff(first, moved);
        Check.That("T11 プレビュー中に選択範囲を動かす → 新しい範囲でプレビューし直す",
            movedDiff > 0.2 && app.SelectionInStatus() == new Rectangle(500, 200, 400, 300), $"差 {movedDiff:F2} ／ {app.Status()}");

        AppDriver.Invoke(AppDriver.ById(section, "FilterApplyButton") ?? throw new InvalidOperationException("［適用］が無い"));
        Waits.Until(app.IsDirty, TimeSpan.FromSeconds(10));
        Thread.Sleep(800);
        string? file = app.Save("PNG");
        if (Check.That("T11 選択範囲にかけた画像を保存できる", file is not null))
        {
            // 保存した PNG と、元の PNG を画素で比べる（範囲は (500, 200) 400×300）
            var before = Pixels.Load(SamplePng);
            var after = Pixels.Load(file!);
            double inside = Pixels.RegionDiff(before, after, 500, 200, 900, 500);
            double outside = Pixels.RegionDiff(before, after, 0, 0, 1200, 200) + Pixels.RegionDiff(before, after, 0, 500, 1200, 900)
                + Pixels.RegionDiff(before, after, 0, 200, 500, 500) + Pixels.RegionDiff(before, after, 900, 200, 1200, 500);
            double border = Pixels.RegionDiff(before, after, 495, 195, 500, 505) + Pixels.RegionDiff(before, after, 900, 195, 905, 505)
                + Pixels.RegionDiff(before, after, 495, 195, 905, 200) + Pixels.RegionDiff(before, after, 495, 500, 905, 505);
            Check.That("T11 ［適用］ → 範囲の中だけが変わり、範囲の外は変わらない（境目のすぐ外側にも色がにじまない）",
                inside > 1 && outside == 0 && border == 0, $"中の差 {inside:F2} ／ 外の差 {outside:F3} ／ 境目の外 5 px の差 {border:F3}");
        }
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T11")]
    public void T11_PreviewEnds_OnSwitchImageOtherFilterAndRotate()
    {
        var app = OpenInWebDeza(SamplePng, UiTestEnvironment.Image("formats", "sample.jpg"));
        app.ActivateDocument("sample.png");
        app.Driver.Menu("フィルター", "ぼかし");
        Thread.Sleep(400);
        var filterCombo = AppDriver.ById(app.FilterSection(), "FilterCombo") ?? throw new InvalidOperationException("フィルターの一覧が無い");
        app.FocusOutOfInputs();
        var plain = app.SettleAndCapture();

        WebDezaApp.SetSlider(app.FilterParameter("半径"), 10);
        var preview = app.SettleAndCapture();
        app.ActivateDocument("sample.jpg");
        Thread.Sleep(500);
        app.ActivateDocument("sample.png");
        var afterSwitch = app.SettleAndCapture();
        bool endsOnSwitch = Pixels.PixelDiff(preview, plain) > 0.3 && Pixels.PixelDiff(afterSwitch, plain) < 0.2;

        WebDezaApp.SetSlider(app.FilterParameter("半径"), 10);
        app.SettleAndCapture();
        app.Driver.SelectCombo(filterCombo, "基本補正");
        var afterOther = app.SettleAndCapture();
        bool endsOnOther = Pixels.PixelDiff(afterOther, plain) < 0.2;

        app.Driver.SelectCombo(filterCombo, "ぼかし");
        WebDezaApp.SetSlider(app.FilterParameter("半径"), 10);
        app.SettleAndCapture();
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_R);
        app.WaitStatus(s => s.Contains("画像: 900×1200"), TimeSpan.FromSeconds(5));
        app.Driver.Menu("編集", "元に戻す");
        app.WaitStatus(s => s.Contains("画像: 1200×900"), TimeSpan.FromSeconds(5));
        var afterRotate = app.SettleAndCapture();
        bool endsOnRotate = Pixels.PixelDiff(afterRotate, plain) < 0.2;

        Check.That("T11 プレビュー中に、ほかの画像に切り替える・ほかのフィルターを選ぶ・回転する → プレビューが消える",
            endsOnSwitch && endsOnOther && endsOnRotate,
            $"切り替え {endsOnSwitch}（{Pixels.PixelDiff(afterSwitch, plain):F2}） ／ ほかのフィルター {endsOnOther}（{Pixels.PixelDiff(afterOther, plain):F2}） ／ 回転 {endsOnRotate}（{Pixels.PixelDiff(afterRotate, plain):F2}）");
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T11")]
    public void T11_LargeImage_PreviewMatchesAppliedAndShowsWaitCursor()
    {
        var app = OpenInWebDeza(UiTestEnvironment.Image("large", "large_8000x6000.jpg"));
        app.Driver.Menu("フィルター", "ぼかし");
        var section = app.FilterSection();
        WebDezaApp.SetSlider(app.FilterParameter("半径"), 20);
        var preview = app.SettleAndCapture(4000);
        SaveShot(preview, "T11_large_preview");

        // 適用の間のカーソルを見るため、マウスをキャンバスの上に置いておく
        Pointer.MoveTo(app.CanvasPoint(0.5, 0.5));
        var apply = AppDriver.ById(section, "FilterApplyButton") ?? throw new InvalidOperationException("［適用］が無い");
        var watch = Stopwatch.StartNew();
        var invoke = Task.Run(() => AppDriver.InvokeNow(apply));
        bool sawWait = false;
        while (watch.Elapsed < TimeSpan.FromSeconds(60) && !SafeIsDirty(app))
        {
            sawWait |= Pointer.IsBusyCursor(Pointer.CurrentCursor());
            Thread.Sleep(50);
        }
        double seconds = watch.Elapsed.TotalSeconds;
        invoke.Wait(TimeSpan.FromSeconds(5));

        // キャンバスの外（メインウィンドウのタイトルバーの中ほど）にマウスを動かしてから取り込む
        var main = app.Main.BoundingRectangle;
        Pointer.MoveTo(new Point(main.X + main.Width / 2, main.Y + 15));
        var applied = app.SettleAndCapture(1500);
        SaveShot(applied, "T11_large_applied");
        double diff = Pixels.PixelDiff(preview, applied);
        Check.Note($"8000×6000 の半径 20 の適用: {seconds:F1} 秒（判定しない）");
        Check.That("T11 large_8000x6000.jpg で、半径 20 のプレビュー（全体表示）と適用の後の見た目がほぼ同じ。適用の間は待機カーソル",
            diff < 1.5 && sawWait, $"差 {diff:F2} ／ 待機カーソル {sawWait}");
        Check.AssertAll();
    }

    private static string Target(FlaUI.Core.AutomationElements.AutomationElement section) =>
        AppDriver.ById(section, "FilterTargetText")?.Name ?? "";

    private static bool SafeIsDirty(WebDezaApp app)
    {
        try { return app.IsDirty(); }
        catch (Exception ex) when (Waits.IsTransient(ex)) { return false; }
    }
}
