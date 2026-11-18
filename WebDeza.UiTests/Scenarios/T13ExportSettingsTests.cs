using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T13 保存の設定（設計書 6.4・6.5）。形式とオプション・ファイル名・背景色が、次に開いたときと起動し直した後も保たれ、
/// キャンセルでは変わらない。出力先の［開く］・［変更…］・消えたとき。formats\sample.jpg（1200×900）を使う
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T13ExportSettingsTests(ITestOutputHelper output) : UiTestBase(output)
{
    private static string Sample => UiTestEnvironment.Image("formats", "sample.jpg");

    [UiFact]
    [Trait("No", "T13")]
    public void T13_FormatsOptionsFileName_KeptOnReopenAndRestartNotOnCancel()
    {
        var app = OpenInWebDeza(Sample);
        var dialog = ExportDialog.Open(app);
        var formats = AppDriver.ComboItems(dialog.FormatCombo);
        Check.That("T13 Ctrl+E → タイトル「保存: sample.jpg」。形式は JPEG / PNG / BMP / TIFF",
            dialog.Title == "保存: sample.jpg" && string.Join("/", formats) == "JPEG/PNG/BMP/TIFF", $"{dialog.Title} ／ {string.Join("/", formats)}");

        dialog.SelectFormat("JPEG");
        var range = WebDezaApp.ParameterSlider(dialog.Quality).Patterns.RangeValue.Pattern;
        bool jpeg = dialog.IsShown("UseQualityRadio") && dialog.IsShown("UseSizeLimitRadio") && range.Minimum.Value == 1 && range.Maximum.Value == 100;
        dialog.SelectFormat("PNG");
        var pngOptions = dialog.Options.FindAllDescendants().Select(e => e.Name).Where(n => n.Length > 0).ToList();
        dialog.SelectFormat("BMP");
        bool bmp = dialog.IsShown("NoOptionsText") && dialog.Text("NoOptionsText") == "この形式にはオプションがありません";
        dialog.SelectFormat("TIFF");
        var tiffItems = AppDriver.ComboItems(dialog.OptionCombo());
        bool tiffLabel = dialog.Options.FindFirstDescendant(cf => cf.ByName("圧縮")) is not null;
        Check.That("T13 JPEG は品質を指定・上限を指定と品質（1〜100）、PNG はインターレース、BMP はオプションなし、TIFF は圧縮（なし / LZW / ZIP）",
            jpeg && pngOptions.Any(n => n.Contains("インターレース")) && bmp && tiffLabel && string.Join("/", tiffItems) == "なし/LZW/ZIP",
            $"JPEG {jpeg} ／ PNG {string.Join(",", pngOptions)} ／ BMP {bmp} ／ TIFF {string.Join("/", tiffItems)}");

        dialog.SelectFormat("JPEG");
        dialog.EnterQuality(60);
        dialog.SelectFormat("PNG");
        dialog.SelectFormat("JPEG");
        Check.That("T13 JPEG の品質を 60 → PNG → JPEG に戻す → 60 のまま", dialog.QualityText() == "60", dialog.QualityText());

        string size = dialog.Text("SizeText");
        var names = new List<string>();
        foreach (var format in new[] { "JPEG", "PNG", "BMP", "TIFF", "JPEG" })
        {
            dialog.SelectFormat(format);
            names.Add(dialog.Text("FileNameText"));
        }
        var pattern = new Regex(@"^sample_\d{8}_\d{6}\.(jpg|png|bmp|tif)$");
        Check.That("T13 大きさが出る。ファイル名は {元の名前}_{yyyyMMdd_HHmmss}.{拡張子} で、形式で拡張子が変わる（TIFF は .tif）",
            size == "1200 × 900 px" && names.All(pattern.IsMatch) && names.Select(Path.GetExtension).SequenceEqual([".jpg", ".png", ".bmp", ".tif", ".jpg"]),
            $"{size} ／ {string.Join(", ", names)}");

        // JPEG・品質 60・背景色 黒で保存する
        dialog.ClickSwatch("#000000");
        dialog.WaitSizeReady();
        string? file = dialog.SaveNewFile();
        string status = app.Status();
        Check.That("T13 JPEG・品質 60・背景色 黒で保存 → 閉じて「保存しました: {ファイル名}（{サイズ}）」。出力先にファイル",
            file is not null && status.EndsWith($"保存しました: {Path.GetFileName(file)}（{ExportDialog.FormatSize(new FileInfo(file).Length)}）"), status);

        dialog = ExportDialog.Open(app);
        bool again = Same(dialog, out var againDetail);
        dialog.SelectFormat("PNG");
        dialog.Cancel();
        dialog = ExportDialog.Open(app);
        bool afterCancel = Same(dialog, out var cancelDetail);
        Check.That("T13 もう一度開く → JPEG・60・黒。PNG に変えて［キャンセル］ → JPEG・60・黒のまま", again && afterCancel, $"{againDetail} ／ {cancelDetail}");

        // ダイアログの大きさと「結果のみ」
        dialog.Window.Patterns.Transform.Pattern.Resize(1000, 700);
        Thread.Sleep(300);
        AppDriver.Select(dialog.Element("ResultOnlyRadio"));
        var resized = dialog.Window.BoundingRectangle;
        dialog.Cancel();
        dialog = ExportDialog.Open(app);
        var reopened = dialog.Window.BoundingRectangle;
        bool resultOnly = AppDriver.IsSelected(dialog.Element("ResultOnlyRadio"));
        Check.That("T13 ダイアログの大きさを変え「結果のみ」にして閉じる → 次に開くと、その大きさと「結果のみ」",
            Math.Abs(reopened.Width - resized.Width) < 3 && Math.Abs(reopened.Height - resized.Height) < 3 && resultOnly,
            $"{resized.Width}×{resized.Height} → {reopened.Width}×{reopened.Height} ／ 結果のみ {resultOnly}");
        AppDriver.Select(dialog.Element("SideBySideRadio"));
        dialog.Cancel();

        // 終了して、設定をそのままに起動し直す
        app.Driver.Menu("ファイル", "終了");
        bool exited = app.Driver.WaitForExit(TimeSpan.FromSeconds(10));
        var restarted = LaunchAnotherWebDeza(Sample);
        Waits.Until(() => restarted.DocumentTitles().Count == 1, TimeSpan.FromSeconds(30));
        Thread.Sleep(800);
        dialog = ExportDialog.Open(restarted);
        bool restored = Same(dialog, out var restartDetail);
        Check.That("T13 終了して起動し直す → JPEG・品質 60・背景色 黒で開く", exited && restored, $"終了 {exited} ／ {restartDetail}");
        dialog.Cancel();
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T13")]
    public void T13_OutputFolder_OpenChangeAndMissing()
    {
        var app = OpenInWebDeza(Sample);
        var dialog = ExportDialog.Open(app);

        // ［開く］ → エクスプローラーで出力先が開く。テストで開いたウィンドウだけを閉じる
        string folderName = Path.GetFileName(UiTestEnvironment.OutputFolder);
        var before = ExplorerWindows(app, folderName).Select(Handle).ToHashSet();
        AppDriver.Invoke(dialog.Element("OpenFolderButton"));
        List<AutomationElement> opened = [];
        bool explorer = Waits.Until(() => (opened = ExplorerWindows(app, folderName).Where(w => !before.Contains(Handle(w))).ToList()).Count > 0,
            TimeSpan.FromSeconds(10));
        foreach (var window in opened)
            try { window.Patterns.Window.Pattern.Close(); } catch (Exception ex) when (Waits.IsTransient(ex)) { }
        Check.That("T13 ［開く］ → エクスプローラーで出力先が開く", explorer, string.Join(", ", opened.Select(w => w.Name)));

        string other = FreshFolder("T13-changed");
        string folderBefore = dialog.OutputFolder();
        dialog.ChooseFolder(other);
        string folderAfter = dialog.OutputFolder();
        Check.That("T13 ［変更…］で別のフォルダ → 出力先の表示が変わる",
            folderAfter == other && folderBefore != other, $"{folderBefore} → {folderAfter}");

        Directory.Delete(other, recursive: true);
        dialog.WaitSizeReady();
        AppDriver.Invoke(dialog.Element("SaveButton"));
        string message = app.Driver.AnswerMessageBox("OK");
        Thread.Sleep(500);
        Check.That("T13 出力先のフォルダを消してから［保存］ → 「出力先フォルダが見つかりません: {パス}」。ダイアログは閉じない",
            message.Contains($"出力先フォルダが見つかりません: {other}") && dialog.IsOpen, message.Replace("\n", " ⏎ "));
        dialog.Cancel();
        Check.AssertAll();
    }

    /// <summary>形式が JPEG・品質 60・背景色 #000000 か</summary>
    private static bool Same(ExportDialog dialog, out string detail)
    {
        string format = AppDriver.ComboText(dialog.FormatCombo), quality = dialog.QualityText(), background = dialog.BackgroundText();
        detail = $"{format}・{quality}・{background}";
        return format == "JPEG" && quality == "60" && background == "#000000";
    }

    /// <summary>エクスプローラーのウィンドウで、タイトルが folderName で始まるもの</summary>
    private static List<AutomationElement> ExplorerWindows(WebDezaApp app, string folderName) =>
        app.Driver.Automation.GetDesktop().FindAllChildren(cf => cf.ByClassName("CabinetWClass"))
            .Where(w => w.Name.StartsWith(folderName)).ToList();

    private static IntPtr Handle(AutomationElement window) => window.Properties.NativeWindowHandle.ValueOrDefault;
}
