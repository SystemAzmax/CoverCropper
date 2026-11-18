using System.Text.Json;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>webDeza の画面の操作（メインウィンドウ・ステータスバー・子ウィンドウ・キャンバス）</summary>
public sealed class WebDezaApp : IDisposable
{
    private WebDezaApp(AppDriver driver) => Driver = driver;

    public AppDriver Driver { get; }

    public Window Main => Driver.Main;

    /// <summary>webDeza を起動する。引数は開くファイルやフォルダ（コマンドライン引数）</summary>
    public static WebDezaApp Launch(params string[] args) =>
        new(AppDriver.Launch(UiTestEnvironment.WebDezaExe, UiTestEnvironment.WebDezaName, args));

    public void Dispose() => Driver.Dispose();

    /// <summary>ステータスバーの文字（例「画像: 1200×900 ／ 56%」）</summary>
    public string Status()
    {
        var bar = Main.FindFirstDescendant(cf => cf.ByControlType(ControlType.StatusBar));
        return bar?.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text))?.Name ?? "";
    }

    /// <summary>ステータスバーの文字が条件に合うまで待つ（合えば true）</summary>
    public bool WaitStatus(Func<string, bool> condition, TimeSpan? timeout = null) =>
        Waits.Until(() => condition(Status()), timeout ?? TimeSpan.FromSeconds(15));

    /// <summary>メインウィンドウのタイトル（例「sample.jpg - webDeza」）</summary>
    public string Title() => Main.Name;

    /// <summary>子ウィンドウのタイトルの一覧（編集があれば末尾に「 *」）</summary>
    public IReadOnlyList<string> DocumentTitles() =>
        Main.FindAllDescendants(cf => cf.ByAutomationId("TitleText")).Select(e => e.Name).ToList();

    /// <summary>アクティブな子ウィンドウ（タイトルが、メインウィンドウのタイトルの画像名と同じもの）</summary>
    public AutomationElement ActiveDocument()
    {
        string title = Title().Replace(" - webDeza", "").TrimEnd('*', ' ');
        foreach (var doc in Main.FindAllDescendants(cf => cf.ByClassName("DocumentWindow")))
        {
            var text = AppDriver.ById(doc, "TitleText");
            if (text is not null && text.Name.TrimEnd('*', ' ') == title) return doc;
        }
        throw new InvalidOperationException("アクティブな子ウィンドウが見つからない: " + title);
    }

    public string ActiveDocumentTitle() => AppDriver.ById(ActiveDocument(), "TitleText")!.Name;

    /// <summary>メニュー［ウィンドウ］の画像の一覧から選んで、アクティブにする</summary>
    public void ActivateDocument(string fileName)
    {
        var menu = AppDriver.ById(Main, "WindowMenu") ?? throw new InvalidOperationException("メニュー［ウィンドウ］が無い");
        var pattern = menu.Patterns.ExpandCollapse.Pattern;
        pattern.Expand();
        Thread.Sleep(400);
        var item = menu.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem))
                       .FirstOrDefault(m => AppDriver.Norm(m.Name).Contains(AppDriver.Norm(fileName)));
        if (item is null)
        {
            pattern.Collapse();
            throw new InvalidOperationException("メニュー［ウィンドウ］に無い: " + fileName);
        }
        AppDriver.Invoke(item);
        Waits.Until(() => Title().StartsWith(fileName), TimeSpan.FromSeconds(5));
    }

    /// <summary>メニュー［ウィンドウ］の画像の一覧（例「1: img_1.jpg」。並べ方の項目は除く）</summary>
    public IReadOnlyList<string> WindowMenuDocuments()
    {
        var menu = AppDriver.ById(Main, "WindowMenu") ?? throw new InvalidOperationException("メニュー［ウィンドウ］が無い");
        var pattern = menu.Patterns.ExpandCollapse.Pattern;
        pattern.Expand();
        Thread.Sleep(600);
        var names = menu.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem)).Select(m => m.Name).ToList();
        pattern.Collapse();
        Thread.Sleep(300);
        var documents = DocumentTitles().Select(t => AppDriver.Norm(t.TrimEnd('*', ' '))).ToHashSet();
        return names.Where(n => documents.Any(d => AppDriver.Norm(n).EndsWith(" " + d))).ToList();
    }

    /// <summary>［ファイル］→［すべて閉じる］。保存していない編集があって確認が出たら［はい］（保存せずに閉じる）</summary>
    public void CloseAll()
    {
        if (DocumentTitles().Count == 0) return;
        Driver.Menu("ファイル", "すべて閉じる");
        Thread.Sleep(400);
        if (Driver.FindMessageBox() is not null) Driver.AnswerMessageBox("はい");
        Waits.Until(() => DocumentTitles().Count == 0, TimeSpan.FromSeconds(10));
    }

    // ------------------------------------------------------------ ドラッグ＆ドロップ

    /// <summary>ワークスペースの中ほど（右の編集パネル 300 px を除いた所の中央）。ファイルを落とす位置</summary>
    public Point WorkspacePoint()
    {
        var r = Main.BoundingRectangle;
        return new Point(r.Left + (r.Width - 300) / 2 + 20, r.Top + r.Height / 2);
    }

    /// <summary>メインウィンドウ全体を取り込む</summary>
    public Pixels CaptureMain() => Pixels.CaptureScreen(Main.BoundingRectangle);

    /// <summary>D&D の強調（ワークスペースの #3A96DD の枠）の画素の数</summary>
    public static int DropHighlightPixels(Pixels pixels) => pixels.CountColor(0x3A, 0x96, 0xDD);

    /// <summary>
    /// ファイルやフォルダを、ワークスペースへ本物の OLE の D&D で運ぶ。drop が false なら落とさずに取り消す。
    /// カーソルがワークスペースの上にあるあいだに、メインウィンドウを取り込む
    /// </summary>
    public FileDragResult DropFiles(bool drop, params string[] paths)
    {
        AppDriver.Activate(Main);
        return FileDrag.Run(paths, WorkspacePoint(), drop, CaptureMain);
    }

    // ------------------------------------------------------------ 保存

    /// <summary>保存のダイアログ（Ctrl+E）を開く</summary>
    public AutomationElement OpenExportDialog()
    {
        Driver.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL, FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_E);
        return Driver.WaitDialog("保存");
    }

    /// <summary>保存のダイアログで［保存］を押し、ダイアログが閉じるのを待つ（閉じたら true）</summary>
    public bool SaveAndWait(AutomationElement dialog, TimeSpan? timeout = null)
    {
        var save = AppDriver.ById(dialog, "SaveButton") ?? throw new InvalidOperationException("［保存］が無い");
        Waits.Until(() => save.IsEnabled, TimeSpan.FromSeconds(30));
        AppDriver.Invoke(save);
        return Waits.Until(() => !Driver.IsOpen(dialog), timeout ?? TimeSpan.FromSeconds(30));
    }

    /// <summary>今の設定（WebDezaSettings.Reset の既定は JPEG・品質 85）で保存し、出力先にできたファイルを返す（できなければ null）</summary>
    public string? SaveWithCurrentSettings()
    {
        var before = Directory.Exists(UiTestEnvironment.OutputFolder)
            ? Directory.GetFiles(UiTestEnvironment.OutputFolder).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var dialog = OpenExportDialog();
        if (!SaveAndWait(dialog)) return null;
        WaitStatus(s => s.Contains("保存しました"), TimeSpan.FromSeconds(10));
        return Directory.Exists(UiTestEnvironment.OutputFolder)
            ? Directory.GetFiles(UiTestEnvironment.OutputFolder).FirstOrDefault(f => !before.Contains(f))
            : null;
    }

    /// <summary>アクティブな子ウィンドウのキャンバスの部分（タイトルバーと縁を除く）を取り込む</summary>
    public Pixels CaptureCanvas()
    {
        AppDriver.Activate(Main);
        Thread.Sleep(500);
        var doc = ActiveDocument();
        var r = doc.BoundingRectangle;
        var close = AppDriver.ById(doc, "CloseButton")?.BoundingRectangle ?? throw new InvalidOperationException("子ウィンドウの閉じるボタンが無い");
        int top = close.Bottom + 2;
        return Pixels.CaptureScreen(new Rectangle(r.X + 4, top, r.Width - 8, r.Bottom - top - 4));
    }
}

/// <summary>webDeza の設定（%APPDATA%\webDeza\settings.json）を、テストの前の状態にする（UiSession が退避して後で戻す）</summary>
public static class WebDezaSettings
{
    public static string FilePath => UiTestEnvironment.SettingsFile(UiTestEnvironment.WebDezaName);

    /// <summary>
    /// 出力先と「開く」のフォルダ、保存の既定、選択範囲の比率、ウィンドウの位置と大きさを決める。
    /// ウィンドウは、主モニターの左上から 240 px 離して置く（画面の端から 200 px 以内をクリックしないため）
    /// </summary>
    public static void Reset(string? lastOpenFolder = null)
    {
        var o = File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? [] : [];
        o["OutputFolder"] = UiTestEnvironment.OutputFolder;
        o["LastOpenFolder"] = lastOpenFolder ?? UiTestEnvironment.ImagesFolder;
        o["Export"] = JsonNode.Parse("""
            {"FormatId":"jpeg","FormatOptions":{"jpeg":{"quality":85},"png":{"interlace":false},"bmp":{},"tiff":{"compression":"lzw"}},
             "UseSizeLimit":false,"SizeLimitKB":300,"Background":"#FFFFFF"}
            """);
        o["SelectionAspect"] = JsonNode.Parse("""{"Kind":"free"}""");
        o["ExportDialogWidth"] = 1100;
        o["ExportDialogHeight"] = 760;
        o["ExportPreviewSideBySide"] = true;
        o["WindowLeft"] = 240;
        o["WindowTop"] = 240;
        o["WindowWidth"] = 1440;
        o["WindowHeight"] = 900;
        o["WindowMaximized"] = false;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
