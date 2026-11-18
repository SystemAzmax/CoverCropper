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
