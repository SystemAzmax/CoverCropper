using System.Text.RegularExpressions;
using FlaUI.Core.WindowsAPI;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T01 元画像の保護（設計書 6.4・6.5）。開く・編集・保存・すべて保存・閉じる・終了の後で、元画像のハッシュと更新日時が変わらない。
/// 出力先を元のフォルダにしても、新しい名前で保存され、元画像は上書きされない。batch の 10 枚（4000×3000）の写しを使う
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T01ProtectOriginalTests(ITestOutputHelper output) : UiTestBase(output)
{
    [UiFact]
    [Trait("No", "T01")]
    public void T01_EditSaveBatchCloseExit_OriginalsUnchanged()
    {
        string originals = FreshFolder("T01-originals");
        foreach (var file in Directory.GetFiles(UiTestEnvironment.Image("batch"), "*.jpg"))
            File.Copy(file, Path.Combine(originals, Path.GetFileName(file)));
        var before = Snapshot(originals);
        Check.Note($"元の画像 {before.Count} 件の、ハッシュと更新日時を記録した");

        string[] three = ["photo_01.jpg", "photo_02.jpg", "photo_03.jpg"];
        var app = OpenInWebDeza(three.Select(n => Path.Combine(originals, n)).ToArray());
        int saved = 0;
        foreach (var name in three)
        {
            app.ActivateDocument(name);
            Blur(app, 4);
            app.SetSelection(200, 200, 3000, 2000);
            app.Driver.Menu("画像", "選択範囲で切り抜き");
            app.WaitStatus(s => s.Contains("画像: 3000×2000"), TimeSpan.FromSeconds(10));
            app.Driver.Menu("画像", "右に 90° 回転");
            app.WaitStatus(s => s.Contains("画像: 2000×3000"), TimeSpan.FromSeconds(10));
            if (app.Save() is not null) saved++;
        }
        var batch = ExportDialog.Open(app, all: true);
        batch.WaitSizeReady(TimeSpan.FromSeconds(60));
        AppDriver.InvokeNow(batch.Element("SaveButton"));
        bool finished = Waits.Until(() => !batch.IsOpen, TimeSpan.FromSeconds(120)) && app.WaitStatus(s => s.Contains("一括保存が終わりました"));
        string m11 = app.Driver.AnswerMessageBox("OK");
        Check.That("T01 元のフォルダの画像を 3 枚開き、ぼかし・切り抜き・回転をして、1 枚ずつ保存・すべて保存",
            saved == 3 && finished && app.Status().Contains("成功 3 件"), $"1 枚ずつ {saved} 件 ／ {app.Status()} ／ M11 {m11.Replace("\n", " ⏎ ")}");

        // 出力先を元のフォルダにして保存する
        app.ActivateDocument("photo_02.jpg");
        app.Driver.Menu("画像", "左右反転");
        var dialog = ExportDialog.Open(app);
        dialog.ChooseFolder(originals);
        string shown = dialog.Text("FileNameText");
        string? added = dialog.SaveNewFile(originals);
        var now = Snapshot(originals);
        Check.That("T01 出力先を元のフォルダにして保存 → 新しい名前（元の名前_yyyyMMdd_HHmmss.jpg）で保存され、元の画像は上書きされない",
            added is not null && Regex.IsMatch(Path.GetFileName(added), @"^photo_02_\d{8}_\d{6}\.jpg$") && before.All(kv => now.GetValueOrDefault(kv.Key) == kv.Value),
            $"増えたファイル {Path.GetFileName(added ?? "（無い）")}（表示 {shown}）");

        // 編集したまま閉じる・すべて閉じる・終了する
        app.Driver.Menu("画像", "上下反転");
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        string m16 = app.Driver.AnswerMessageBox("はい");
        Waits.Until(() => app.DocumentTitles().Count == 2, TimeSpan.FromSeconds(5));
        app.ActivateDocument("photo_01.jpg");
        app.Driver.Menu("画像", "左右反転");
        app.Driver.Menu("ファイル", "すべて閉じる");
        string m16All = app.Driver.AnswerMessageBox("はい");
        Waits.Until(() => app.DocumentTitles().Count == 0, TimeSpan.FromSeconds(5));
        app.Driver.Menu("ファイル", "終了");
        bool exited = app.Driver.WaitForExit(TimeSpan.FromSeconds(10));
        Check.That("T01 編集したまま閉じる（M16 で［はい］）・すべて閉じる・終了する",
            m16.Contains("保存していない編集") && m16All.Contains("1 枚") && exited, $"{m16.Replace("\n", " ⏎ ")} ／ {m16All.Replace("\n", " ⏎ ")} ／ 終了 {exited}");

        var after = Snapshot(originals);
        var changed = before.Where(kv => after.GetValueOrDefault(kv.Key) != kv.Value).Select(kv => Path.GetFileName(kv.Key)).ToList();
        var newFiles = after.Keys.Where(k => !before.ContainsKey(k)).Select(Path.GetFileName).ToList();
        Check.That("T01 記録を比べる → 元の画像（10 件）に差が無い。差は元のフォルダに新しく保存したファイルだけ",
            before.Count == 10 && changed.Count == 0 && newFiles.Count == 1,
            $"変わった元の画像 {changed.Count} 件 {string.Join(", ", changed)} ／ 新しいファイル {string.Join(", ", newFiles)}");
        Check.AssertAll();
    }

    /// <summary>ぼかしを、半径を決めて適用する</summary>
    private static void Blur(WebDezaApp app, double radius)
    {
        app.Driver.Menu("フィルター", "ぼかし");
        WebDezaApp.SetSlider(app.FilterParameter("半径"), radius);
        Thread.Sleep(800);
        AppDriver.Invoke(AppDriver.ById(app.FilterSection(), "FilterApplyButton") ?? throw new InvalidOperationException("［適用］が無い"));
        Waits.Until(app.IsDirty, TimeSpan.FromSeconds(30));
        Thread.Sleep(300);
    }
}
