using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>T12 元に戻す（設計書 6.4・6.5）。履歴の名前と順番、やり直し、画像ごとの履歴、保存と *、履歴に積まない操作</summary>
[Collection(UiCollection.Name)]
public sealed class T12UndoTests(ITestOutputHelper output) : UiTestBase(output)
{
    [UiFact]
    [Trait("No", "T12")]
    public void T12_UndoRedo_NamesOrderPerImageDirtyAndNonHistoryOperations()
    {
        // A = sample.png、B = sample.jpg
        var app = OpenInWebDeza(UiTestEnvironment.Image("formats", "sample.png"), UiTestEnvironment.Image("formats", "sample.jpg"));
        app.ActivateDocument("sample.png");
        var start = app.CaptureCanvas();

        app.Driver.Menu("フィルター", "ぼかし");
        WebDezaApp.SetSlider(app.FilterParameter("半径"), 5);
        Thread.Sleep(600);
        AppDriver.Invoke(AppDriver.ById(app.FilterSection(), "FilterApplyButton") ?? throw new InvalidOperationException("［適用］が無い"));
        Waits.Until(app.IsDirty, TimeSpan.FromSeconds(5));
        app.SetSelection(100, 100, 800, 600);
        app.Driver.Menu("画像", "選択範囲で切り抜き");
        app.WaitStatus(s => s.Contains("画像: 800×600"), TimeSpan.FromSeconds(5));
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_R);
        app.WaitStatus(s => s.Contains("画像: 600×800"), TimeSpan.FromSeconds(5));
        app.Driver.Menu("画像", "左右反転");
        app.Driver.Menu("画像", "画像サイズ");
        var dialog = app.Driver.WaitDialog("画像サイズ");
        AppDriver.SetValue(AppDriver.ById(dialog, "WidthInput") ?? throw new InvalidOperationException("幅の入力欄が無い"), "300");
        Thread.Sleep(200);
        AppDriver.Invoke(AppDriver.ById(dialog, "OkButton") ?? throw new InvalidOperationException("［OK］が無い"));
        Check.That("T12 ぼかしの適用・切り抜き・右に 90° 回転・左右反転・画像サイズの変更を、この順に行う",
            app.WaitStatus(s => s.Contains("画像: 300×400"), TimeSpan.FromSeconds(5)), app.Status());

        string header = app.UndoHeader();
        string tip = AppDriver.AllByType(app.Main, ControlType.Button).FirstOrDefault(b => b.Name == "元に戻す")?.HelpText ?? "";
        Check.That("T12 ［編集］メニュー → 「元に戻す: 画像サイズの変更」。ツールバーの［元に戻す］のツールチップも同じ",
            AppDriver.Norm(header).StartsWith("元に戻す: 画像サイズの変更") && tip.StartsWith("元に戻す: 画像サイズの変更"), $"{header} ／ {tip}");

        var undoNames = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            Thread.Sleep(500);
            undoNames.Add(UndoName(app));
        }
        var (undoEnabled, _) = app.UndoRedoEnabled();
        var end = app.CaptureCanvas();
        string[] expectedUndo = ["元に戻す: 左右反転", "元に戻す: 右に 90° 回転", "元に戻す: 切り抜き", "元に戻す: ぼかし", "元に戻す"];
        Check.That("T12 Ctrl+Z を 5 回 → 1 段ずつ戻り、名前が「左右反転」「右に 90° 回転」「切り抜き」「ぼかし」。最後は開いた時の画像で * が消え、［元に戻す］が無効",
            undoNames.SequenceEqual(expectedUndo) && !undoEnabled && !app.IsDirty() && app.Status().Contains("画像: 1200×900") && Pixels.ThumbDiff(start, end) < 0.2,
            string.Join(" → ", undoNames) + $" ／ 有効 {undoEnabled} ／ {app.ActiveDocumentTitle()} ／ 開いた時との差 {Pixels.ThumbDiff(start, end):F2}");

        var redoNames = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            if (i < 4) app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Y);
            else app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_Z);
            Thread.Sleep(500);
            redoNames.Add(UndoName(app));
        }
        string[] expectedRedo = ["元に戻す: ぼかし", "元に戻す: 切り抜き", "元に戻す: 右に 90° 回転", "元に戻す: 左右反転", "元に戻す: 画像サイズの変更"];
        Check.That("T12 Ctrl+Y を 4 回と Ctrl+Shift+Z を 1 回 → 1 段ずつやり直す",
            redoNames.SequenceEqual(expectedRedo) && app.Status().Contains("画像: 300×400"), string.Join(" → ", redoNames));

        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Thread.Sleep(400);
        bool redoBefore = app.UndoRedoEnabled().Redo;
        app.Driver.Menu("画像", "上下反転");
        Thread.Sleep(400);
        Check.That("T12 戻した後に別の操作をする → ［やり直し］が無効になる", redoBefore && !app.UndoRedoEnabled().Redo);

        string aBefore = app.UndoHeader();
        app.ActivateDocument("sample.jpg");
        app.Driver.Menu("画像", "左右反転");
        Thread.Sleep(300);
        string bAfterEdit = app.UndoHeader();
        app.ActivateDocument("sample.png");
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Thread.Sleep(400);
        string aAfter = app.UndoHeader();
        app.ActivateDocument("sample.jpg");
        string bAfter = app.UndoHeader();
        app.ActivateDocument("sample.png");
        Check.That("T12 画像 A を編集 → 画像 B をアクティブにして編集 → A に戻って Ctrl+Z → A だけが 1 段戻り、B は変わらない",
            AppDriver.Norm(aBefore).Contains("上下反転") && AppDriver.Norm(aAfter).Contains("左右反転") && bAfterEdit == bAfter && AppDriver.Norm(bAfter).Contains("左右反転"),
            $"A {aBefore} → {aAfter} ／ B {bAfterEdit} → {bAfter}");

        bool dirtyBefore = app.IsDirty();
        app.Save();
        bool dirtyAfterSave = app.IsDirty();
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Thread.Sleep(400);
        bool dirtyAfterUndo = app.IsDirty();
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Y);
        Thread.Sleep(400);
        bool dirtyAfterRedo = app.IsDirty();
        Check.That("T12 編集 → 保存 → * が消える → Ctrl+Z → * が付く → Ctrl+Y → * が消える",
            dirtyBefore && !dirtyAfterSave && dirtyAfterUndo && !dirtyAfterRedo, $"{dirtyBefore} → {dirtyAfterSave} → {dirtyAfterUndo} → {dirtyAfterRedo}");

        // 選択範囲（キャンバスのドラッグと数値の入力）とズームは、履歴に積まない。1200×900 の B で行う
        app.ActivateDocument("sample.jpg");
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_D);
        app.Driver.Menu("選択範囲", "領域選択");
        string historyBefore = app.UndoHeader();
        Pointer.Drag(app.CanvasPoint(0.35, 0.35), app.CanvasPoint(0.55, 0.55));
        var drawn = app.SelectionInStatus();
        Rectangle? dragged = null;
        if (drawn is { } d)
        {
            // 選択範囲の中から右下へドラッグして動かす（大きさは変わらない）
            var from = app.CanvasPoint(0.45, 0.45);
            Pointer.Drag(from, new Point(from.X + 60, from.Y + 40));
            dragged = app.SelectionInStatus();
        }
        Check.That("T12 キャンバスでドラッグして選択範囲を作り、範囲の中をドラッグして動かす（大きさはそのまま）",
            drawn is { Width: > 50, Height: > 50 } && dragged is { } m && m.Size == drawn.Value.Size && m.X > drawn.Value.X && m.Y > drawn.Value.Y,
            $"作った {drawn} ／ 動かした {dragged}");
        app.Driver.EnterValue(app.Input("SelectionXInput"), "50");
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_PLUS);
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0);
        string historyAfter = app.UndoHeader();
        Check.That("T12 選択範囲を作る・動かす（ドラッグ・数値）・ズームする → 履歴に積まれない（［元に戻す］の名前が変わらない）",
            historyBefore == historyAfter && app.SelectionInStatus()?.X == 50, $"{historyBefore} → {historyAfter} ／ {app.Status()}");
        Check.AssertAll();
    }

    /// <summary>［元に戻す］の名前（アクセスキーを除く。例「元に戻す: 左右反転」）</summary>
    private static string UndoName(WebDezaApp app) => AppDriver.Norm(app.UndoHeader()).Replace("(U)", "");
}
