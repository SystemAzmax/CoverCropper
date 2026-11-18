using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>T19 閉じる・終了の確認（設計書 6.4・6.5）。M16・M17 の確認と、ウィンドウの位置と大きさの保存</summary>
[Collection(UiCollection.Name)]
public sealed class T19CloseTests(ITestOutputHelper output) : UiTestBase(output)
{
    private const string M17 = "保存していない編集があります。終了しますか？";

    private static string Jpeg => UiTestEnvironment.Image("formats", "sample.jpg");

    private static string Png => UiTestEnvironment.Image("formats", "sample.png");

    [UiFact]
    [Trait("No", "T19")]
    public void T19_CloseImage_AsksOnlyWhenEdited()
    {
        var app = OpenInWebDeza(Jpeg, Png);
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        Thread.Sleep(800);
        Check.That("T19 編集していない画像を閉じる（Ctrl+W） → 確かめずに閉じる",
            app.Driver.FindMessageBox() is null && app.DocumentTitles().Count == 1, string.Join(",", app.DocumentTitles()));

        // 残っているのは sample.jpg
        app.Driver.Menu("画像", "左右反転");
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        string m16 = app.Driver.AnswerMessageBox("いいえ");
        bool kept = app.DocumentTitles().Count == 1;
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        app.Driver.AnswerMessageBox("はい");
        bool closed = Waits.Until(() => app.DocumentTitles().Count == 0, TimeSpan.FromSeconds(5));
        Check.That("T19 編集した画像を閉じる → M16「「sample.jpg」には保存していない編集があります。閉じますか？」。［いいえ］で閉じない、［はい］で閉じる",
            m16 == "「sample.jpg」には保存していない編集があります。閉じますか？" && kept && closed, Flat(m16));

        var second = OpenInWebDeza(Jpeg, Png);
        second.Driver.Menu("画像", "左右反転");
        second.ActivateDocument("sample.jpg");
        second.Driver.Menu("画像", "左右反転");
        second.Driver.Menu("ファイル", "すべて閉じる");
        string all = second.Driver.AnswerMessageBox("いいえ");
        Check.That("T19 編集した画像が 2 枚あるときに［すべて閉じる］ → 「保存していない編集がある画像が 2 枚あります。すべて閉じますか？」（［いいえ］で閉じない）",
            all == "保存していない編集がある画像が 2 枚あります。すべて閉じますか？" && second.DocumentTitles().Count == 2, Flat(all));
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T19")]
    public void T19_Exit_AsksWithM17FromMenuCloseButtonAndAltF4()
    {
        var app = OpenInWebDeza(Jpeg);
        app.Driver.Menu("画像", "左右反転");

        app.Driver.Menu("ファイル", "終了");
        string fromMenu = app.Driver.AnswerMessageBox("いいえ");
        bool aliveAfterMenu = !app.Driver.WaitForExit(TimeSpan.FromMilliseconds(800));
        app.Driver.RequestClose();
        string fromClose = app.Driver.AnswerMessageBox("いいえ");
        bool aliveAfterClose = !app.Driver.WaitForExit(TimeSpan.FromMilliseconds(800));
        // Alt+F4 は確かめる手順そのもの（ウィンドウを手前にするための Alt キーの技ではない）
        app.Driver.Press(VirtualKeyShort.ALT, VirtualKeyShort.F4);
        string fromAltF4 = app.Driver.AnswerMessageBox("はい");
        bool exited = app.Driver.WaitForExit(TimeSpan.FromSeconds(10));
        Check.That("T19 編集した画像があるときに［ファイル］→［終了］・ウィンドウの［×］・Alt+F4 → M17。［いいえ］で終了しない、［はい］で終了する",
            fromMenu == M17 && fromClose == M17 && fromAltF4 == M17 && aliveAfterMenu && aliveAfterClose && exited,
            $"{fromMenu} ／ {fromClose} ／ {fromAltF4} ／ 終了しない {aliveAfterMenu} {aliveAfterClose} ／ 終了した {exited}");

        var saved = OpenInWebDeza(Jpeg);
        saved.Driver.Menu("画像", "左右反転");
        saved.Save();
        saved.Driver.Menu("ファイル", "終了");
        Check.That("T19 編集した画像を保存してから終了する → 確かめずに終了する", saved.Driver.WaitForExit(TimeSpan.FromSeconds(5)));

        var undone = OpenInWebDeza(Jpeg);
        undone.Driver.Menu("画像", "左右反転");
        undone.Save();
        undone.Driver.Menu("編集", "元に戻す");
        undone.Driver.Menu("ファイル", "終了");
        string afterUndo = undone.Driver.AnswerMessageBox("はい");
        Check.That("T19 保存した後に元に戻してから終了する → M17 で確かめる",
            afterUndo == M17 && undone.Driver.WaitForExit(TimeSpan.FromSeconds(10)), Flat(afterUndo));
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T19")]
    public void T19_ExitWithoutEdits_RestoresWindowPlacement()
    {
        // 画面の端から 200 px 以上離れた位置に置く（タイトルバーをクリックすることがあるため）
        var placed = new Rectangle(300, 280, 1300, 820);
        var first = LaunchWebDeza();
        var transform = first.Main.Patterns.Transform.Pattern;
        transform.Move(placed.X, placed.Y);
        transform.Resize(placed.Width, placed.Height);
        Thread.Sleep(400);
        var before = first.Main.BoundingRectangle;
        first.Driver.Menu("ファイル", "終了");
        bool quiet = first.Driver.WaitForExit(TimeSpan.FromSeconds(5));

        // 設定をそのままにして起動し直す
        var second = LaunchAnotherWebDeza();
        var restored = second.Main.BoundingRectangle;
        second.Main.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        Thread.Sleep(500);
        second.Driver.Menu("ファイル", "終了");
        second.Driver.WaitForExit(TimeSpan.FromSeconds(5));

        var third = LaunchAnotherWebDeza();
        var state = third.Main.Patterns.Window.Pattern.WindowVisualState.Value;
        third.Main.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        Thread.Sleep(500);
        var normal = third.Main.BoundingRectangle;
        Check.That("T19 編集が無いときに終了する → 確かめずに終了する。起動し直すと、ウィンドウの位置と大きさ（最大化も）が前回のまま",
            quiet && Near(before, restored) && state == WindowVisualState.Maximized && Near(before, normal),
            $"{before} → {restored} ／ 最大化 {state} ／ 元に戻すと {normal}");
        third.Driver.Menu("ファイル", "終了");
        third.Driver.WaitForExit(TimeSpan.FromSeconds(5));
        Check.AssertAll();
    }

    private static bool Near(Rectangle a, Rectangle b) =>
        Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) <= 2 && Math.Abs(a.Width - b.Width) <= 2 && Math.Abs(a.Height - b.Height) <= 2;

    private static string Flat(string text) => text.Replace("\n", " ⏎ ");
}
