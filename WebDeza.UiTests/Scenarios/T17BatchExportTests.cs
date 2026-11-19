using System.Diagnostics;
using System.Text.RegularExpressions;
using FlaUI.Core.WindowsAPI;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T17 一括保存（設計書 6.4・6.5）。進捗・実行中に押せないもの・M11 の合計・上限、中止（［中止］と Esc）、途中で出力先を消したとき。
/// batch の 10 枚（4000×3000）を使う。JPEG の 10 件は 2〜3 秒で終わるので、進捗と中止は PNG で確かめる（設計書 6.5）
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T17BatchExportTests(ITestOutputHelper output) : UiTestBase(output)
{
    private static string[] BatchFiles => Enumerable.Range(1, 10).Select(i => UiTestEnvironment.Image("batch", $"photo_{i:00}.jpg")).ToArray();

    private static string Output => UiTestEnvironment.OutputFolder;

    [UiFact]
    [Trait("No", "T17")]
    public void T17_ProgressLockedWhileRunningM11TotalAndSizeLimit()
    {
        var app = OpenInWebDeza(BatchFiles);
        app.ActivateDocument("photo_03.jpg");
        app.Driver.Menu("画像", "左右反転");
        app.ActivateDocument("photo_10.jpg");
        var dialog = ExportDialog.Open(app, all: true);
        dialog.WaitSizeReady(TimeSpan.FromSeconds(60));
        string saveText = dialog.Element("SaveButton").Name, headline = dialog.Headline();
        Check.That("T17 Ctrl+Shift+E → タイトル「すべて保存（10 件）」、ボタン［10 件を保存］、プレビューが出る",
            dialog.Title == "すべて保存（10 件）" && saveText == "10 件を保存" && headline.StartsWith("圧縮後"), $"{dialog.Title} ／ {saveText} ／ {headline}");

        dialog.SelectFormat("PNG");
        dialog.WaitSizeReady(TimeSpan.FromSeconds(60));
        var before = ExportDialog.FilesIn(Output);
        // 進捗は 1 件 1 秒ほどで変わるので、読む要素は先に探しておく（毎回ツリーから探すと読み逃す）
        var save = dialog.Element("SaveButton");
        var format = dialog.FormatCombo;
        var cancel = dialog.Find("CancelButton");
        FlaUI.Core.AutomationElements.AutomationElement? progressText = null, cancelBatch = null, progressBar = null;
        var watch = Stopwatch.StartNew();
        AppDriver.InvokeNow(save);
        var progress = new List<string>();
        bool locked = true, cancelShown = false, bar = false;
        for (int round = 0; dialog.IsOpen && watch.Elapsed < TimeSpan.FromMinutes(5); round++)
        {
            try
            {
                progressText ??= dialog.Find("BatchProgressText");
                string text = progressText?.Name ?? "";
                if (text.Length > 0 && (progress.Count == 0 || progress[^1] != text)) progress.Add(text);
                if (round % 3 == 1 && text.Length > 0)
                {
                    cancelBatch ??= dialog.Find("CancelBatchButton");
                    progressBar ??= dialog.Find("BatchProgressBar");
                    locked &= !format.IsEnabled && (cancel is null || !cancel.IsEnabled || cancel.IsOffscreen)
                              && !save.IsEnabled && !app.Driver.IsMainEnabled();
                    cancelShown |= cancelBatch is { } c && c.IsEnabled && !c.IsOffscreen;
                    bar |= progressBar is { } b && b.Patterns.RangeValue.Pattern.Value.Value > 0;
                }
            }
            catch (Exception ex) when (Waits.IsTransient(ex)) { }
            Thread.Sleep(50);
        }
        Check.Note($"読んだ進捗の表示: {string.Join(" | ", progress)}");
        Check.Note($"PNG の 10 件の一括保存: {watch.Elapsed.TotalSeconds:F1} 秒（判定しない）");
        app.WaitStatus(s => s.Contains("一括保存が終わりました"), TimeSpan.FromSeconds(30));
        string m11 = app.Driver.AnswerMessageBox("OK", TimeSpan.FromSeconds(30));
        var files = ExportDialog.NewFilesIn(Output, before);
        var m = Regex.Match(app.Status(), @"一括保存が終わりました。成功 (\d+) 件 ／ 失敗 (\d+) 件 ／ 合計 (.+)$");
        string sum = ExportDialog.FormatSize(files.Sum(f => new FileInfo(f).Length));
        Check.That("T17 ［10 件を保存］ → プログレスバーと「3 / 10: photo_03.jpg」のような進捗。実行中は［中止］のほかは押せず、メインウィンドウも操作できない",
            progress.Count(p => Regex.IsMatch(p, @"^\d+ / 10: photo_\d\d\.jpg$")) >= 3 && bar && locked && cancelShown,
            $"進捗 {string.Join(" | ", progress.Take(4))} …（{progress.Count} 種） ／ バー {bar} ／ 押せない {locked} ／ 中止 {cancelShown}");
        app.ActivateDocument("photo_03.jpg");
        Check.That("T17 終わると閉じ、M11「成功 10 件 ／ 失敗 0 件 ／ 合計 {サイズ}」。合計は 10 ファイルの合計と合う。編集した画像の * が消える",
            m.Success && m.Groups[1].Value == "10" && m.Groups[2].Value == "0" && m.Groups[3].Value == sum && files.Count == 10
                && m11 == m.Value && !app.IsDirty(),
            $"{app.Status()} ／ メッセージ「{m11}」 ／ ファイル {files.Count} 件 合計 {sum} ／ {app.ActiveDocumentTitle()}");

        dialog = ExportDialog.Open(app, all: true);
        dialog.SelectFormat("JPEG");
        dialog.UseSizeLimit(300);
        dialog.WaitSizeReady(TimeSpan.FromSeconds(60));
        before = ExportDialog.FilesIn(Output);
        AppDriver.InvokeNow(dialog.Element("SaveButton"));
        Waits.Until(() => !dialog.IsOpen, TimeSpan.FromMinutes(5));
        app.Driver.AnswerMessageBox("OK", TimeSpan.FromSeconds(30));
        files = ExportDialog.NewFilesIn(Output, before);
        long largest = files.Count == 0 ? 0 : files.Max(f => new FileInfo(f).Length);
        Check.That("T17 「上限を指定」・300 KB で一括保存 → 10 ファイルとも 307,200 バイト以下",
            files.Count == 10 && largest <= 307_200, $"{files.Count} 件 ／ 最大 {largest:N0} バイト");
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T17")]
    public void T17_Cancel_WithButtonAndEsc()
    {
        var app = OpenInWebDeza(BatchFiles);
        var dialog = StartPngBatch(app, out var before);
        Waits.Until(() => dialog.ProgressText().StartsWith("2 / 10"), TimeSpan.FromSeconds(60));
        AppDriver.InvokeNow(dialog.Element("CancelBatchButton"));
        string cancelling = "";
        Waits.Until(() => (cancelling = dialog.ProgressText()).Contains("中止"), TimeSpan.FromSeconds(3));
        Waits.Until(() => !dialog.IsOpen, TimeSpan.FromSeconds(60));
        app.WaitStatus(s => s.Contains("中止しました"));
        Thread.Sleep(800);
        bool noBox = app.Driver.FindMessageBox() is null;
        var files = ExportDialog.NewFilesIn(Output, before);
        var m = Regex.Match(app.Status(), @"一括保存を中止しました（(\d+) 件保存済み）。");
        Check.That("T17 2 件目で［中止］ → 「中止しています…」の後に止まり、M12「一括保存を中止しました（n 件保存済み）。」。出力先に n 件",
            cancelling.Contains("中止しています") && m.Success && int.Parse(m.Groups[1].Value) == files.Count && files.Count is >= 1 and <= 3 && noBox,
            $"「{cancelling}」 ／ {app.Status()} ／ ファイル {files.Count} 件 ／ メッセージボックスなし {noBox}");

        dialog = StartPngBatch(app, out before);
        Waits.Until(() => dialog.ProgressText().StartsWith("2 / 10"), TimeSpan.FromSeconds(60));
        app.Driver.PressTo(dialog.Window, VirtualKeyShort.ESCAPE);
        bool closed = Waits.Until(() => !dialog.IsOpen, TimeSpan.FromSeconds(60));
        files = ExportDialog.NewFilesIn(Output, before);
        Check.That("T17 Esc でも中止できる（10 件より前で止まる）",
            closed && app.WaitStatus(s => s.Contains("中止しました"), TimeSpan.FromSeconds(10)) && files.Count < 10, $"{app.Status()} ／ ファイル {files.Count} 件");
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T17")]
    public void T17_OutputFolderRemovedWhileRunning_StopsWithM06()
    {
        var app = OpenInWebDeza(BatchFiles);
        string gone = FreshFolder("T17-gone");
        var dialog = ExportDialog.Open(app, all: true);
        dialog.SelectFormat("PNG");
        dialog.WaitSizeReady(TimeSpan.FromSeconds(60));
        dialog.ChooseFolder(gone);
        bool chosen = dialog.OutputFolder() == gone;
        AppDriver.InvokeNow(dialog.Element("SaveButton"));
        Waits.Until(() => dialog.ProgressText().StartsWith("2 / 10"), TimeSpan.FromSeconds(60));
        // 書いている途中のファイルがあると消せないことがあるので、少し待ってやり直す
        for (int i = 0; i < 50 && Directory.Exists(gone); i++)
        {
            if (TryFileOperation(() => Directory.Delete(gone, recursive: true)) is not null) Thread.Sleep(100);
        }
        bool deleted = !Directory.Exists(gone);
        string message = Waits.Until(() => app.Driver.FindMessageBox() is not null, TimeSpan.FromSeconds(60)) ? app.Driver.AnswerMessageBox("OK") : "";
        Thread.Sleep(1000);
        string status = app.Status();
        var m = Regex.Match(status, @"成功 (\d+) 件 ／ 失敗 (\d+) 件");
        Check.That("T17 途中で出力先のフォルダを消す → そこで止まり、「出力先フォルダが見つかりません: {パス}」",
            chosen && deleted && message == $"出力先フォルダが見つかりません: {gone}" && m.Success && int.Parse(m.Groups[1].Value) + int.Parse(m.Groups[2].Value) < 10,
            $"出力先を変えた {chosen} ／ 消せた {deleted} ／ メッセージ「{message.Replace("\n", " ⏎ ")}」 ／ {status} ／ 保存のダイアログ {(dialog.IsOpen ? "開いている" : "閉じた")}");
        if (dialog.IsOpen) dialog.Cancel();
        Check.AssertAll();
    }

    /// <summary>すべて保存のダイアログを開き、PNG で始める（始める前の出力先のファイルを before に）</summary>
    private static ExportDialog StartPngBatch(WebDezaApp app, out HashSet<string> before)
    {
        var dialog = ExportDialog.Open(app, all: true);
        dialog.SelectFormat("PNG");
        dialog.WaitSizeReady(TimeSpan.FromSeconds(60));
        before = ExportDialog.FilesIn(Output);
        AppDriver.InvokeNow(dialog.Element("SaveButton"));
        return dialog;
    }
}
