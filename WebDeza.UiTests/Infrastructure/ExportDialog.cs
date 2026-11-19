using System.Globalization;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>
/// 保存のダイアログ（Ctrl+E）と、すべて保存のダイアログ（Ctrl+Shift+E）の操作（設計書 4.6・F-08〜F-11）。
/// 形式・オプション・背景色・上限・保存サイズ・出力先・プレビュー
/// </summary>
public sealed class ExportDialog
{
    private static readonly TimeSpan ShortWait = TimeSpan.FromSeconds(5);

    private readonly IntPtr handle;

    private ExportDialog(WebDezaApp app, AutomationElement window)
    {
        App = app;
        Window = window;
        handle = window.Properties.NativeWindowHandle.ValueOrDefault;
    }

    public WebDezaApp App { get; }

    /// <summary>ダイアログのウィンドウ</summary>
    public AutomationElement Window { get; }

    /// <summary>保存のダイアログ（all なら、すべて保存のダイアログ）を開く</summary>
    public static ExportDialog Open(WebDezaApp app, bool all = false)
    {
        AutomationElement window;
        if (all)
        {
            app.Driver.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_E);
            window = app.Driver.WaitDialog("すべて保存");
        }
        else
        {
            window = app.OpenExportDialog();
        }
        // 開いた直後は、プレビューと保存サイズの計算が始まるところ
        Thread.Sleep(600);
        return new ExportDialog(app, window);
    }

    /// <summary>タイトル（例「保存: sample.jpg」「すべて保存（10 件）」）</summary>
    public string Title => Window.Name;

    /// <summary>
    /// まだ開いているか。ウィンドウのハンドルで調べる（AppDriver.IsOpen は、開いている画像が多いとメインウィンドウの中を
    /// たどるのに時間がかかり、一括保存の進捗を読み逃すため）
    /// </summary>
    public bool IsOpen => handle != IntPtr.Zero ? Native.IsWindow(handle) : App.Driver.IsOpen(Window);

    /// <summary>x:Name で探す。無ければ例外</summary>
    public AutomationElement Element(string automationId) =>
        AppDriver.ById(Window, automationId) ?? throw new InvalidOperationException("保存のダイアログに無い: " + automationId);

    /// <summary>x:Name で探す。無い（畳まれている）ときは null</summary>
    public AutomationElement? Find(string automationId) => AppDriver.ById(Window, automationId);

    /// <summary>文字の表示（無い・畳まれているときは ""）</summary>
    public string Text(string automationId) => Find(automationId)?.Name ?? "";

    /// <summary>その要素が見えているか（ツリーにあり、画面の外でない）</summary>
    public bool IsShown(string automationId) =>
        Find(automationId) is { } e && !e.IsOffscreen && !e.BoundingRectangle.IsEmpty;

    // ------------------------------------------------------------ 形式とオプション

    public AutomationElement FormatCombo => Element("FormatCombo");

    /// <summary>形式を選ぶ（一覧の表示。例 "PNG"）</summary>
    public void SelectFormat(string format)
    {
        App.Driver.SelectCombo(FormatCombo, format, Window);
        Thread.Sleep(300);
    }

    /// <summary>形式ごとのオプションの入力欄が並ぶ所</summary>
    public AutomationElement Options => Element("OptionsList");

    /// <summary>オプションの行（例 "品質"）</summary>
    public AutomationElement Option(string label) => WebDezaApp.Parameter(Options, label);

    /// <summary>オプションのコンボボックス（TIFF の圧縮）</summary>
    public AutomationElement OptionCombo() =>
        Waits.For(() => Options.FindFirstDescendant(cf => cf.ByControlType(ControlType.ComboBox)), ShortWait, "オプションのコンボボックス");

    /// <summary>JPEG の品質の行</summary>
    public AutomationElement Quality => Option("品質");

    /// <summary>JPEG の品質の入力欄の文字</summary>
    public string QualityText() => AppDriver.GetValue(WebDezaApp.ParameterInput(Quality));

    /// <summary>JPEG の品質の入力欄が読み取り専用か（上限を指定したとき）</summary>
    public bool IsQualityReadOnly() => WebDezaApp.ParameterInput(Quality).Patterns.Value.Pattern.IsReadOnly.Value;

    /// <summary>JPEG の品質を入力欄に入れて確定する</summary>
    public void EnterQuality(int quality) =>
        App.Driver.EnterValue(WebDezaApp.ParameterInput(Quality), quality.ToString(CultureInfo.InvariantCulture), Window);

    /// <summary>「上限を指定」を選び、上限（KB）を入れて確定する</summary>
    public void UseSizeLimit(int kilobytes)
    {
        AppDriver.Select(Element("UseSizeLimitRadio"));
        App.Driver.EnterValue(Element("SizeLimitInput"), kilobytes.ToString(CultureInfo.InvariantCulture), Window);
    }

    // ------------------------------------------------------------ 背景色

    /// <summary>背景色の色見本（"#FFFFFF"・"#000000"・"#808080"）を押す</summary>
    public void ClickSwatch(string color)
    {
        var item = Waits.For(() => AppDriver.AllByType(Window, ControlType.DataItem).FirstOrDefault(d => d.Name == color), ShortWait, "色見本 " + color);
        var button = item.FindFirstChild(cf => cf.ByControlType(ControlType.Button)) ?? throw new InvalidOperationException("色見本のボタンが無い: " + color);
        AppDriver.Invoke(button);
        Thread.Sleep(300);
    }

    /// <summary>背景色の入力欄（#RRGGBB）</summary>
    public AutomationElement BackgroundInput => Element("ColorInput");

    public string BackgroundText() => AppDriver.GetValue(BackgroundInput);

    /// <summary>背景色を入力欄に入れて確定する（例 "#FF8800"）</summary>
    public void EnterBackground(string color) => App.Driver.EnterValue(BackgroundInput, color, Window);

    // ------------------------------------------------------------ 保存サイズ

    /// <summary>保存サイズの表示（例「182.4 KB（元ファイルの 40%）」「計算中…」）</summary>
    public string FileSizeText() => Element("FileSizeText").Name;

    /// <summary>保存サイズの計算が終わるまで待つ（終われば true）</summary>
    public bool WaitSizeReady(TimeSpan? timeout = null)
    {
        Thread.Sleep(300);
        return Waits.Until(() =>
        {
            string text = FileSizeText();
            return text.Length > 0 && !text.Contains("計算中");
        }, timeout ?? TimeSpan.FromSeconds(20));
    }

    /// <summary>保存サイズのツールチップのバイト数（例「186,778 バイト」。無ければ -1）</summary>
    public long TooltipBytes()
    {
        var m = Regex.Match(Element("FileSizeText").HelpText, @"([\d,]+) バイト");
        return m.Success ? long.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture) : -1;
    }

    /// <summary>
    /// バイト数の表示（アプリの FileSizeFormatter と同じ書き方。設計書 F-09）。
    /// UI テストはアプリのコードを呼ばないので、同じ決まりで書く
    /// </summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        double kilobytes = Math.Round(bytes / 1024.0, 1, MidpointRounding.AwayFromZero);
        if (kilobytes < 1024) return kilobytes.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        return Math.Round(bytes / 1048576.0, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
    }

    // ------------------------------------------------------------ 保存・キャンセル

    /// <summary>［保存］を押し、ダイアログが閉じるのを待つ（閉じたら true）</summary>
    public bool SaveAndWait(TimeSpan? timeout = null) => App.SaveAndWait(Window, timeout);

    /// <summary>
    /// ［保存］を押し、folder（省けば UI テストの出力先）に新しくできたファイルを返す（できなければ null）
    /// </summary>
    public string? SaveNewFile(string? folder = null, TimeSpan? timeout = null)
    {
        folder ??= UiTestEnvironment.OutputFolder;
        var before = FilesIn(folder);
        if (!SaveAndWait(timeout)) return null;
        App.WaitStatus(s => s.Contains("保存しました"), TimeSpan.FromSeconds(10));
        string? added = null;
        Waits.Until(() => (added = FilesIn(folder).FirstOrDefault(f => !before.Contains(f))) is not null, TimeSpan.FromSeconds(10));
        return added;
    }

    public void Cancel()
    {
        AppDriver.ClickButton(Window, "キャンセル");
        Waits.Until(() => !IsOpen, ShortWait);
        Thread.Sleep(300);
    }

    /// <summary>フォルダの中のファイル（無ければ空）。保存の前に取っておき、後で増えたファイルを調べる</summary>
    public static HashSet<string> FilesIn(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];

    /// <summary>before の後に、folder に増えたファイル</summary>
    public static List<string> NewFilesIn(string folder, HashSet<string> before) =>
        FilesIn(folder).Where(f => !before.Contains(f)).Order(StringComparer.OrdinalIgnoreCase).ToList();

    // ------------------------------------------------------------ 一括保存の進捗

    /// <summary>一括保存の進捗の表示（例「3 / 10: photo_03.jpg」「中止しています…」。実行中でなければ ""）</summary>
    public string ProgressText() => Text("BatchProgressText");

    // ------------------------------------------------------------ 出力先

    /// <summary>出力先の表示（全体はツールチップにある）</summary>
    public string OutputFolder() => Element("OutputFolderText").HelpText;

    /// <summary>［変更…］で出力先のフォルダを選ぶ（フォルダーの選択の入力欄に入れて Enter）</summary>
    public void ChooseFolder(string folder)
    {
        AppDriver.Invoke(Element("ChangeFolderButton"));
        App.Driver.PickFolder(folder);
    }

    // ------------------------------------------------------------ プレビュー

    /// <summary>プレビューの倍率の表示（例「100%」）</summary>
    public string ZoomText() => Text("PreviewZoomText");

    /// <summary>プレビューの見出し（例「圧縮後  JPEG 品質 85 ／ 182.4 KB」）</summary>
    public string Headline() => Text("PreviewHeadlineText");

    /// <summary>
    /// プレビューの左（圧縮前）と右（圧縮後）の絵の領域（画面の物理ピクセル）。「結果のみ」のときは左が Empty。
    /// 絵のキャンバスは UI Automation のツリーに無いので、見出し・［保存］・右の設定の位置から決める
    /// </summary>
    public (Rectangle Left, Rectangle Right) PreviewPanes()
    {
        var head = Element("PreviewHeadlineText").BoundingRectangle;
        var save = Element("SaveButton").BoundingRectangle;
        var settings = (Window.FindFirstChild(cf => cf.ByClassName("ScrollViewer")) ?? throw new InvalidOperationException("右の設定が無い")).BoundingRectangle;
        int top = head.Bottom + 6, bottom = save.Top - 16;
        var right = new Rectangle(head.Left + 4, top, settings.Left - head.Left - 20, bottom - top);
        var before = AppDriver.AllByType(Window, ControlType.Text).FirstOrDefault(t => t.Name == "圧縮前");
        var left = before is null || before.IsOffscreen || before.BoundingRectangle.IsEmpty
            ? Rectangle.Empty
            : new Rectangle(before.BoundingRectangle.Left + 4, top, head.Left - before.BoundingRectangle.Left - 20, bottom - top);
        return (left, right);
    }

    /// <summary>
    /// 左右のプレビューを取り込んで比べる（±2 px ずらした中でいちばん小さい差。Pixels.ShiftedDiff）。
    /// カーソルがプレビューの上にあるとツールチップが写り込むので、先にプレビューの下へ動かす
    /// </summary>
    public double PaneDiff()
    {
        var (left, right) = PreviewPanes();
        var area = Rectangle.Union(left.IsEmpty ? right : left, right);
        area.Inflate(40, 40);
        if (area.Contains(FlaUI.Core.Input.Mouse.Position))
        {
            var below = left.IsEmpty ? right : left;
            Pointer.MoveTo(new Point(below.Left + 10, below.Bottom + 60));
            Thread.Sleep(800);
        }
        return Pixels.ShiftedDiff(Pixels.CaptureScreen(left), Pixels.CaptureScreen(right));
    }
}
