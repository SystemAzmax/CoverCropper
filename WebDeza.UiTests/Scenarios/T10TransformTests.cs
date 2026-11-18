using FlaUI.Core.WindowsAPI;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>T10 回転・反転・画像サイズ（設計書 6.4・6.5）。formats\sample.jpg（1200×900。黄色い矢印が上を向く）を使う</summary>
[Collection(UiCollection.Name)]
public sealed class T10TransformTests(ITestOutputHelper output) : UiTestBase(output)
{
    private static string Sample => UiTestEnvironment.Image("formats", "sample.jpg");

    [UiFact]
    [Trait("No", "T10")]
    public void T10_RotateAndFlip_PictureSelectionAndSavedFile()
    {
        var app = OpenInWebDeza(Sample);
        var original = app.CaptureCanvas();
        SaveShot(original, "T10_original");

        // 100% にしてから回し、全体表示に戻るかを見る
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Waits.Until(() => app.ZoomPercent() == 100, TimeSpan.FromSeconds(3));
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_R);
        app.WaitStatus(s => s.Contains("画像: 900×1200"), TimeSpan.FromSeconds(5));
        Thread.Sleep(500);
        int fit = app.ZoomPercent();
        var rotated = app.CaptureCanvas();
        SaveShot(rotated, "T10_rotate_right");
        var arrow = rotated.Arrow(out var arrowDetail);
        Check.That("T10 Ctrl+R → 900×1200 になり、矢印が右を向き、全体表示に戻る（100% から変わる）",
            app.Status().Contains("画像: 900×1200") && arrow == ArrowDirection.Right && fit is > 0 and < 100,
            $"{app.Status()} ／ 矢印 {arrow} {arrowDetail}");

        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_R);
        app.WaitStatus(s => s.Contains("画像: 1200×900"), TimeSpan.FromSeconds(5));
        var back = app.SettleAndCapture(300);
        double backDiff = Pixels.ThumbDiff(original, back);
        Check.That("T10 Ctrl+Shift+R → 1200×900 に戻る（絵も元どおり）", app.Status().Contains("画像: 1200×900") && backDiff < 3, $"元との差 {backDiff:F1}");

        app.Driver.Menu("画像", "左右反転");
        var flippedH = app.SettleAndCapture(600);
        SaveShot(flippedH, "T10_flip_h");
        double mirrorDiff = Pixels.ThumbDiff(original.MirrorHorizontal(), flippedH), changedH = Pixels.ThumbDiff(original, flippedH);
        app.Driver.Menu("画像", "上下反転");
        var flippedHV = app.SettleAndCapture(600);
        SaveShot(flippedHV, "T10_flip_hv");
        double verticalDiff = Pixels.ThumbDiff(flippedH.MirrorVertical(), flippedHV), changedV = Pixels.ThumbDiff(flippedH, flippedHV);
        Check.That("T10 ［左右反転］ → 鏡に映した絵。［上下反転］ → 上下逆の絵",
            mirrorDiff < 4 && changedH > 10 && verticalDiff < 4 && changedV > 10,
            $"左右: 鏡像との差 {mirrorDiff:F1}（元との差 {changedH:F1}） ／ 上下: {verticalDiff:F1}（{changedV:F1}）");
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Waits.Until(() => !app.IsDirty(), TimeSpan.FromSeconds(3));

        app.SetSelection(100, 100, 300, 200);
        bool hadSelection = app.SelectionInStatus() == new Rectangle(100, 100, 300, 200);
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_R);
        app.WaitStatus(s => s.Contains("画像: 900×1200"), TimeSpan.FromSeconds(5));
        Check.That("T10 選択範囲を作ってから回転する → 選択範囲は解除される", hadSelection && app.SelectionInStatus() is null, app.Status());
        app.Driver.Menu("編集", "元に戻す");
        app.WaitStatus(s => s.Contains("画像: 1200×900"), TimeSpan.FromSeconds(5));

        app.SetSelection(100, 100, 300, 200);
        app.Driver.Menu("画像", "左右反転");
        Check.That("T10 左寄りの選択範囲 (100, 100) 300×200 で左右反転 → 右寄りの (800, 100) に移る（画像の中身と一緒に動く）",
            Waits.Until(() => app.SelectionInStatus() == new Rectangle(800, 100, 300, 200), TimeSpan.FromSeconds(3)), app.Status());
        app.Driver.Menu("編集", "元に戻す");
        app.Driver.Menu("選択範囲", "選択の取り消し");
        Waits.Until(() => app.SelectionInStatus() is null && !app.IsDirty(), TimeSpan.FromSeconds(3));

        // 回転と反転をした画像を保存し、読み直して向きを確かめる（ほかのビューアーで開く代わり）
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_R);
        app.WaitStatus(s => s.Contains("画像: 900×1200"), TimeSpan.FromSeconds(5));
        app.Driver.Menu("画像", "左右反転");
        var canvas = app.SettleAndCapture(500);
        string? file = app.Save("PNG");
        if (Check.That("T10 回転と反転をした画像を保存できる", file is not null, Path.GetFileName(file ?? "")))
        {
            var saved = Pixels.Load(file!);
            var expected = Pixels.Load(Sample).RotateRight().MirrorHorizontal();
            double diff = Pixels.ThumbDiff(saved, expected);
            var savedArrow = saved.Arrow(out _);
            var canvasArrow = canvas.Arrow(out _);
            Check.That("T10 保存したファイルは、右に 90° 回して左右反転した絵（キャンバスと同じ向き）",
                saved.Width == 900 && saved.Height == 1200 && diff < 2 && savedArrow == canvasArrow && savedArrow != ArrowDirection.None,
                $"{saved.Width}×{saved.Height} ／ 期待との差 {diff:F2} ／ 矢印 ファイル {savedArrow} キャンバス {canvasArrow}");
        }
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T10")]
    public void T10_ImageSize_KeepAspectValidationAndSavedSize()
    {
        var app = OpenInWebDeza(Sample);

        // 縮小しておき、画像サイズを変えた後に全体表示に戻るかを見る
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        for (int i = 0; i < 4; i++) app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_MINUS);
        Thread.Sleep(300);
        int zoomBefore = app.ZoomPercent();

        var dialog = OpenImageSize(app);
        string current = Text(dialog, "CurrentSizeText");
        Check.That("T10 ［画像］→［画像サイズ…］ → 今の大きさ 1200 × 900 px。「縦横比を保つ」はオン",
            current == "1200 × 900 px" && AppDriver.IsOn(Element(dialog, "KeepAspectCheck")), current);
        AppDriver.SetValue(Element(dialog, "WidthInput"), "600");
        Thread.Sleep(300);
        string height = AppDriver.GetValue(Element(dialog, "HeightInput")), result = Text(dialog, "ResultText");
        AppDriver.Invoke(Element(dialog, "OkButton"));
        Waits.Until(() => !app.Driver.IsOpen(dialog), TimeSpan.FromSeconds(3));
        app.WaitStatus(s => s.Contains("画像: 600×450"), TimeSpan.FromSeconds(5));
        Thread.Sleep(500);
        int zoomAfter = app.ZoomPercent();
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        Thread.Sleep(300);
        int zoomFit = app.ZoomPercent();
        Check.That("T10 幅に 600 → 高さが 450、変更後「600 × 450 px（50%）」。［OK］ → 600×450 になり、全体表示に戻る",
            height == "450" && result == "600 × 450 px（50%）" && app.Status().Contains("画像: 600×450") && zoomAfter == zoomFit && zoomAfter != zoomBefore,
            $"高さ {height} ／ {result} ／ {app.Status()} ／ 倍率 前 {zoomBefore}% 後 {zoomAfter}% 全体表示 {zoomFit}%");
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        app.WaitStatus(s => s.Contains("画像: 1200×900"), TimeSpan.FromSeconds(5));

        dialog = OpenImageSize(app);
        AppDriver.Toggle(Element(dialog, "KeepAspectCheck"));
        AppDriver.SetValue(Element(dialog, "WidthInput"), "600");
        Thread.Sleep(300);
        height = AppDriver.GetValue(Element(dialog, "HeightInput"));
        result = Text(dialog, "ResultText");
        Check.That("T10 「縦横比を保つ」をオフにして幅だけを変える → 高さは変わらず、倍率が幅と高さで別々に出る",
            height == "900" && result == "600 × 900 px（幅 50%・高さ 100%）", $"高さ {height} ／ {result}");

        var ok = Element(dialog, "OkButton");
        var reasons = new List<string>();
        bool allDisabled = true;
        foreach (var value in new[] { "0", "30001", "abc" })
        {
            AppDriver.SetValue(Element(dialog, "WidthInput"), value);
            Thread.Sleep(300);
            allDisabled &= !ok.IsEnabled;
            reasons.Add($"{value}: {Text(dialog, "ResultText")}");
        }
        Check.That("T10 幅に 0、30001、abc → ［OK］が無効になり、理由が出る",
            allDisabled && reasons.All(r => !r.Contains(" px（")), string.Join(" ／ ", reasons));
        AppDriver.ClickButton(dialog, "キャンセル");
        Waits.Until(() => !app.Driver.IsOpen(dialog), TimeSpan.FromSeconds(3));

        dialog = OpenImageSize(app);
        if (!AppDriver.IsOn(Element(dialog, "KeepAspectCheck"))) AppDriver.Toggle(Element(dialog, "KeepAspectCheck"));
        AppDriver.SetValue(Element(dialog, "WidthInput"), "640");
        Thread.Sleep(300);
        AppDriver.Invoke(Element(dialog, "OkButton"));
        app.WaitStatus(s => s.Contains("画像: 640×480"), TimeSpan.FromSeconds(5));
        string? file = app.Save();
        var saved = file is null ? null : Pixels.Load(file);
        Check.That("T10 画像サイズを変えた画像を保存する → 保存したファイルが 640×480",
            saved is { Width: 640, Height: 480 }, saved is null ? "保存できない" : $"{saved.Width}×{saved.Height}");
        Check.AssertAll();
    }

    private static FlaUI.Core.AutomationElements.AutomationElement OpenImageSize(WebDezaApp app)
    {
        app.Driver.Menu("画像", "画像サイズ");
        return app.Driver.WaitDialog("画像サイズ");
    }

    private static FlaUI.Core.AutomationElements.AutomationElement Element(FlaUI.Core.AutomationElements.AutomationElement dialog, string id) =>
        AppDriver.ById(dialog, id) ?? throw new InvalidOperationException("画像サイズのダイアログに無い: " + id);

    private static string Text(FlaUI.Core.AutomationElements.AutomationElement dialog, string id) => Element(dialog, id).Name;
}
