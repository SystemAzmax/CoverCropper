using System.Text.RegularExpressions;
using FlaUI.Core.WindowsAPI;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// R02 CoverCropper の回帰確認（設計書 6.3・6.5）。読み込み（D&D・開く）・枠の移動（矢印キー・ドラッグ）・保存・
/// 保存先の変更・ウィンドウ位置の保存。設定とログの場所（R03）
/// </summary>
[Collection(UiCollection.Name)]
public sealed class R02CoverCropperTests(ITestOutputHelper output) : UiTestBase(output)
{
    private static string Output => UiTestEnvironment.CoverCropperOutputFolder;

    private static string Portrait => UiTestEnvironment.Image("exif", "portrait_orientation6.jpg");

    [UiFact]
    [Trait("No", "R02")]
    public void R02_LoadMoveFrameAndSave()
    {
        var app = LaunchCoverCropper();

        // 読み込み: D&D（4:3 と、EXIF の向きが 6 の縦長）・開く（透過 PNG）
        app.DropFiles(UiTestEnvironment.Image("formats", "sample.jpg"));
        app.WaitStatus(s => s.Contains("リサイズ後"));
        string landscape = app.Status();
        app.DropFiles(Portrait);
        app.WaitStatus(s => s.Contains("900×1200"));
        string portrait = app.Status();
        var size = Regex.Match(portrait, @"リサイズ後: (\d+)×(\d+) ／ 切り抜き位置: \((\d+), (\d+)\)");
        int resizedHeight = size.Success ? int.Parse(size.Groups[2].Value) : -1;
        bool centered = size.Success && size.Groups[1].Value == "1024" && int.Parse(size.Groups[4].Value) == (resizedHeight - 768) / 2;
        app.Driver.OpenViaDialog(UiTestEnvironment.Image("transparent"), "transparent.png");
        app.WaitStatus(s => s.Contains("800×600"));
        string transparent = app.Status();
        string? file = app.Save();
        var corner = file is null ? default : Pixels.Load(file).At(5, 5);
        Check.That("R02 D&D・開く（Ctrl+O）で読み込む → 比率を保ってリサイズし、1024×768 の枠が中央。EXIF の向きが反映。透過 PNG は白で合成して保存",
            landscape.Contains("元: 1200×900 → リサイズ後: 1024×768 ／ 切り抜き位置: (0, 0)") && portrait.Contains("元: 900×1200") && centered
                && transparent.Contains("元: 800×600 → リサイズ後: 1024×768") && file is not null && corner is { R: > 245, G: > 245, B: > 245 },
            $"{landscape} ／ {portrait} ／ {transparent} ／ 透過の所 ({corner.R},{corner.G},{corner.B})");

        // 枠を動かす（縦長の写真: リサイズ後 1024×1365 で、y は 0〜597）
        app.DropFiles(Portrait);
        app.WaitStatus(s => s.Contains("900×1200"));
        var p0 = app.CropPosition();
        app.Driver.Press(VirtualKeyShort.DOWN);
        var p1 = app.CropPosition();
        app.Driver.Press(VirtualKeyShort.SHIFT, VirtualKeyShort.DOWN);
        var p2 = app.CropPosition();
        app.Driver.Press(VirtualKeyShort.UP);
        var p3 = app.CropPosition();
        // 画面の端に近づかないよう、枠の中（プレビューの中ほど）から ±150 px だけ動かすのを、枠が止まるまで繰り返す
        var center = app.PreviewCenter();
        var p4 = DragUntilStop(app, center, new Point(center.X, center.Y + 150));
        var p5 = DragUntilStop(app, center, new Point(center.X, center.Y - 150));
        app.Driver.Press(VirtualKeyShort.HOME);
        var p6 = app.CropPosition();
        int maxY = resizedHeight - 768;
        Check.That("R02 枠を矢印キー（1 px）・Shift+矢印（10 px）・マウスのドラッグで動かす → 画像の外に出ない。Home で中央に戻る",
            p0 is { } a && p1?.Y == a.Y + 1 && p2?.Y == a.Y + 11 && p3?.Y == a.Y + 10 && p4?.Y == maxY && p5?.Y == 0 && p6 == p0,
            $"{p0} → ↓{p1} → Shift+↓{p2} → ↑{p3} → 下へドラッグ{p4}（下限 {maxY}） → 上へドラッグ{p5} → Home{p6}");

        // Ctrl+S を続けて 2 回（保存している間の Ctrl+S は受け付けないので、1 回目が終わってから送る）。
        // 同じ秒に保存されるよう、秒が変わった直後に始める
        var before = ExportDialog.FilesIn(Output);
        Waits.Until(() => DateTime.Now.Millisecond < 100, TimeSpan.FromSeconds(2));
        app.Save();
        app.Save();
        Thread.Sleep(500);
        var saved = ExportDialog.NewFilesIn(Output, before).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToList();
        var savedSize = saved.Count > 0 ? Pixels.ImageSize(Path.Combine(Output, saved[0])) : Size.Empty;
        var pattern = new Regex(@"^portrait_orientation6_\d{8}_\d{6}(_2)?\.jpg$");
        bool withSuffix = saved.Count == 2 && saved[1] == saved[0].Replace(".jpg", "_2.jpg");
        Check.Note(withSuffix ? "同じ秒に 2 回保存され、2 つ目に _2 が付いた" : "2 回の保存は別の秒になった（_2 は付かない）");
        Check.That("R02 Ctrl+S → 元の名前_yyyyMMdd_HHmmss.jpg（1024×768・JPEG）。続けて保存すると上書きせず、同じ秒なら _2 が付く",
            saved.Count == 2 && saved.All(pattern.IsMatch) && savedSize == new Size(1024, 768), string.Join(", ", saved));
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "R02")]
    public void R02_OutputFolderAndWindowKeptAfterRestart_SettingsAndLogInAppData()
    {
        var app = LaunchCoverCropper();
        app.DropFiles(UiTestEnvironment.Image("formats", "sample.jpg"));
        app.WaitStatus(s => s.Contains("リサイズ後"));

        string other = FreshFolder("R02-output");
        app.ChangeOutputFolder(other);
        string? file = app.Save(other);
        var placed = new Rectangle(360, 300, 1200, 860);
        var transform = app.Main.Patterns.Transform.Pattern;
        transform.Move(placed.X, placed.Y);
        transform.Resize(placed.Width, placed.Height);
        Thread.Sleep(400);
        var before = app.Main.BoundingRectangle;
        app.Driver.RequestClose();
        bool exited = app.Driver.WaitForExit(TimeSpan.FromSeconds(10));
        var again = LaunchAnotherCoverCropper();
        var after = again.Main.BoundingRectangle;
        bool folderKept = again.ShowsOutputFolder(other);
        Check.That("R02 保存先を変える → 変えた保存先に保存。閉じて起動し直しても、保存先とウィンドウの位置・大きさが前回のまま",
            file is not null && exited && folderKept && Near(before, after),
            $"保存 {Path.GetFileName(file ?? "（無い）")} ／ 保存先の表示 {folderKept} ／ {before} → {after}");

        // 読めないファイルを落として、ログを書かせる（CoverCropper は失敗したときだけログを書く）
        string appData = UiTestEnvironment.AppDataFolder(UiTestEnvironment.CoverCropperName);
        string log = Path.Combine(appData, "logs", "app.log");
        var since = DateTime.Now.AddSeconds(-1);
        again.DropFiles(UiTestEnvironment.Image("broken", "broken1.jpg"));
        string message = again.Driver.AnswerMessageBox("OK");
        Thread.Sleep(500);
        bool logged = File.Exists(log) && File.GetLastWriteTime(log) >= since && ReadShared(log).Contains("broken1.jpg");
        bool settings = File.Exists(Path.Combine(appData, "settings.json"));
        Check.That("R02 設定とログが %APPDATA%\\CoverCropper\\ にある（R03）。読めないファイルを落とすと、メッセージを出し、logs\\app.log に書く",
            settings && logged, $"settings.json {settings} ／ ログに書いた {logged} ／ メッセージ「{message.Replace("\n", " ⏎ ")}」");
        again.Driver.RequestClose();
        again.Driver.WaitForExit(TimeSpan.FromSeconds(10));
        Check.AssertAll();
    }

    /// <summary>枠を from から to へドラッグするのを、切り抜き位置が変わらなくなるまで（最大 10 回）繰り返す</summary>
    private static Point? DragUntilStop(CoverCropperApp app, Point from, Point to)
    {
        var last = app.CropPosition();
        for (int i = 0; i < 10; i++)
        {
            AppDriver.Activate(app.Main);
            Pointer.Drag(from, to);
            var now = app.CropPosition();
            if (now == last) return now;
            last = now;
        }
        return last;
    }

    /// <summary>アプリが書いている途中でも読めるように、共有で読む</summary>
    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    private static bool Near(Rectangle a, Rectangle b) =>
        Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) <= 2 && Math.Abs(a.Width - b.Width) <= 2 && Math.Abs(a.Height - b.Height) <= 2;
}
