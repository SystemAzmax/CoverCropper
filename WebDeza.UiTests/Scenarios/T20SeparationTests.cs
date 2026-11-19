using System.Text.Json.Nodes;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T20 CoverCropper との分離（設計書 6.4・6.5）。2 つのアプリを同時に動かし、片方の操作で、もう片方の設定・ログ
/// （%APPDATA% の下のフォルダ）が変わらない。同時に動かしている間、どちらも普通に動く
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T20SeparationTests(ITestOutputHelper output) : UiTestBase(output)
{
    [UiFact]
    [Trait("No", "T20")]
    public void T20_WebDezaAndCoverCropper_DoNotTouchEachOthersSettingsAndLogs()
    {
        string ccData = UiTestEnvironment.AppDataFolder(UiTestEnvironment.CoverCropperName);
        string wdData = UiTestEnvironment.AppDataFolder(UiTestEnvironment.WebDezaName);
        var cc = LaunchCoverCropper();
        var ccBefore = Snapshot(ccData);

        // webDeza: 読めないファイル（ログを書く）・開く・比率を変える・保存する（出力先を変える）・終了する
        var since = DateTime.Now.AddSeconds(-1);
        var wd = LaunchWebDeza(UiTestEnvironment.Image("broken", "broken1.jpg"), UiTestEnvironment.Image("formats", "sample.jpg"));
        string message = wd.Driver.AnswerMessageBox("OK", TimeSpan.FromSeconds(15));
        Waits.Until(() => wd.DocumentTitles().Count == 1, TimeSpan.FromSeconds(15));
        bool bothRunning = !cc.Driver.HasExited && !wd.Driver.HasExited;
        wd.Driver.SelectCombo(wd.Input("SelectionAspectCombo"), "16:9");
        string other = FreshFolder("T20-webdeza");
        var dialog = ExportDialog.Open(wd);
        dialog.ChooseFolder(other);
        string? saved = dialog.SaveNewFile(other);
        wd.Driver.Menu("ファイル", "終了");
        bool exited = wd.Driver.WaitForExit(TimeSpan.FromSeconds(10));
        var ccAfter = Snapshot(ccData);
        var wdSettings = JsonNode.Parse(File.ReadAllText(WebDezaSettings.FilePath));
        bool wdChanged = wdSettings?["OutputFolder"]?.ToString() == other && wdSettings?["SelectionAspect"]?.ToJsonString().Contains("16") == true;
        string logs = Path.Combine(wdData, "logs");
        bool wdLogged = Directory.Exists(logs) && Directory.GetFiles(logs).Any(f => File.GetLastWriteTime(f) >= since);
        var ccDiff = SnapshotDiff(ccBefore, ccAfter);
        Check.That("T20 CoverCropper を動かしたまま、webDeza で開く（読めないファイルでログを書く）・比率を変える・保存する（出力先を変える）・終了する → %APPDATA%\\CoverCropper に差が無い",
            bothRunning && message.Contains("対応していないファイル形式") && saved is not null && exited && wdChanged && wdLogged && ccDiff.Count == 0,
            $"両方動いている {bothRunning} ／ CoverCropper の差 {(ccDiff.Count == 0 ? "なし" : string.Join(", ", ccDiff))} ／ webDeza の設定が変わった {wdChanged} ／ webDeza のログ {wdLogged}");

        // CoverCropper: 読み込む・保存する・保存先を変える（webDeza は動かしておく）
        wd = LaunchAnotherWebDeza();
        Thread.Sleep(1000);
        var wdBefore = Snapshot(wdData);
        cc.DropFiles(UiTestEnvironment.Image("formats", "sample.jpg"));
        cc.WaitStatus(s => s.Contains("リサイズ後"));
        string? first = cc.Save();
        string ccOther = FreshFolder("T20-covercropper");
        cc.ChangeOutputFolder(ccOther);
        string? second = cc.Save(ccOther);
        Thread.Sleep(500);
        var wdAfter = Snapshot(wdData);
        var wdDiff = SnapshotDiff(wdBefore, wdAfter);
        bool ccChanged = JsonNode.Parse(File.ReadAllText(CoverCropperSettings.FilePath))?["OutputFolder"]?.ToString() == ccOther;
        Check.That("T20 webDeza を動かしたまま、CoverCropper で読み込む・保存する・保存先を変える → %APPDATA%\\webDeza に差が無い",
            first is not null && second is not null && ccChanged && wdDiff.Count == 0,
            $"webDeza の差 {(wdDiff.Count == 0 ? "なし" : string.Join(", ", wdDiff))} ／ CoverCropper の設定が変わった {ccChanged}");

        // 同時に動かしている間、どちらも普通に動く
        wd.Driver.OpenViaDialog(UiTestEnvironment.Image("formats"), "sample.png");
        bool wdOpened = Waits.Until(() => wd.DocumentTitles().Count == 1, TimeSpan.FromSeconds(15));
        wd.Driver.Menu("画像", "右に 90° 回転");
        bool wdRotated = wd.WaitStatus(s => s.Contains("画像: 900×1200"), TimeSpan.FromSeconds(10));
        cc.DropFiles(UiTestEnvironment.Image("formats", "sample.png"));
        bool ccLoaded = Waits.Until(() => cc.Title().StartsWith("sample.png"), TimeSpan.FromSeconds(15));
        Check.That("T20 2 つを同時に動かしている間、どちらも普通に動く（webDeza で開いて回転、CoverCropper で読み込む）",
            wdOpened && wdRotated && ccLoaded && !cc.Driver.HasExited && !wd.Driver.HasExited,
            $"webDeza {wd.Status()} ／ CoverCropper {cc.Title()} {cc.Status()}");

        wd.Driver.Menu("ファイル", "終了");
        wd.Driver.AnswerMessageBox("はい");
        wd.Driver.WaitForExit(TimeSpan.FromSeconds(10));
        cc.Driver.RequestClose();
        cc.Driver.WaitForExit(TimeSpan.FromSeconds(10));
        Check.AssertAll();
    }
}
