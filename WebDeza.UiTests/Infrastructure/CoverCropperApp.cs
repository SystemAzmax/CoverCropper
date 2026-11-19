using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>CoverCropper の画面の操作（ステータスバー・切り抜き枠・D&D・保存・保存先）。CoverCropper 設計書のとおり</summary>
public sealed class CoverCropperApp : IDisposable
{
    private CoverCropperApp(AppDriver driver) => Driver = driver;

    public AppDriver Driver { get; }

    public Window Main => Driver.Main;

    /// <summary>CoverCropper を起動する</summary>
    public static CoverCropperApp Launch() =>
        new(AppDriver.Launch(UiTestEnvironment.CoverCropperExe, UiTestEnvironment.CoverCropperName, []));

    public void Dispose() => Driver.Dispose();

    /// <summary>ステータスバーの文字（例「元: 1200×900 → リサイズ後: 1024×768 ／ 切り抜き位置: (0, 0)」）</summary>
    public string Status()
    {
        var bar = Main.FindFirstDescendant(cf => cf.ByControlType(ControlType.StatusBar));
        return bar?.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text))?.Name ?? "";
    }

    public bool WaitStatus(Func<string, bool> condition, TimeSpan? timeout = null) =>
        Waits.Until(() => condition(Status()), timeout ?? TimeSpan.FromSeconds(15));

    /// <summary>ウィンドウのタイトル（例「sample.jpg - CoverCropper」）</summary>
    public string Title() => Main.Name;

    /// <summary>ステータスバーの切り抜き位置（無ければ null）</summary>
    public Point? CropPosition()
    {
        var m = Regex.Match(Status(), @"切り抜き位置: \((\d+), (\d+)\)");
        return m.Success ? new Point(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
    }

    /// <summary>プレビューの中ほど（ウィンドウの中央より少し下。切り抜き枠の中で、ファイルを落とす位置にも使う）</summary>
    public Point PreviewCenter()
    {
        var r = Main.BoundingRectangle;
        return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2 + 20);
    }

    /// <summary>ファイルを、プレビューの中ほどへ本物の OLE の D&D で運んで落とす</summary>
    public FileDragResult DropFiles(params string[] paths)
    {
        AppDriver.Activate(Main);
        return FileDrag.Run(paths, PreviewCenter(), drop: true);
    }

    /// <summary>
    /// Ctrl+S で保存し、folder（省けば CoverCropper の保存先）に新しくできたファイルを返す（できなければ null）。
    /// 保存が終わる（その名前で「保存しました」が出る）まで待つ。保存している間の Ctrl+S は受け付けられないため
    /// </summary>
    public string? Save(string? folder = null)
    {
        folder ??= UiTestEnvironment.CoverCropperOutputFolder;
        var before = ExportDialog.FilesIn(folder);
        Driver.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        string? added = null;
        Waits.Until(() => (added = ExportDialog.NewFilesIn(folder, before).FirstOrDefault()) is not null, TimeSpan.FromSeconds(15));
        if (added is not null) WaitStatus(s => s.Contains("保存しました: " + Path.GetFileName(added)), TimeSpan.FromSeconds(10));
        return added;
    }

    /// <summary>［変更…］で保存先のフォルダを選ぶ</summary>
    public void ChangeOutputFolder(string folder)
    {
        AppDriver.ClickButton(Main, "変更");
        Driver.PickFolder(folder);
    }

    /// <summary>保存先の表示に、そのフォルダが出ているか</summary>
    public bool ShowsOutputFolder(string folder) =>
        Main.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text).And(cf.ByName(folder))) is not null;
}

/// <summary>CoverCropper の設定（%APPDATA%\CoverCropper\settings.json）を、テストの前の状態にする（UiSession が退避して後で戻す）</summary>
public static class CoverCropperSettings
{
    public static string FilePath => UiTestEnvironment.SettingsFile(UiTestEnvironment.CoverCropperName);

    /// <summary>
    /// 保存先・「開く」のフォルダ・ウィンドウの位置と大きさを決める。ウィンドウは主モニターの左上から 300×280 px 離して置く
    /// （画面の端から 200 px 以内をクリックしないため。webDeza（240, 240）とも少しずらす）
    /// </summary>
    public static void Reset()
    {
        var o = File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? [] : [];
        o["OutputFolder"] = UiTestEnvironment.CoverCropperOutputFolder;
        o["LastOpenFolder"] = UiTestEnvironment.ImagesFolder;
        o["WindowLeft"] = 300;
        o["WindowTop"] = 280;
        o["WindowWidth"] = 1280;
        o["WindowHeight"] = 900;
        o["WindowMaximized"] = false;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
