using System.Diagnostics;
using FlaUI.Core.WindowsAPI;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T18 大きな画像（設計書 6.4・6.5）。large\large_8000x6000.jpg で、開く・ズーム・パン・フィルターのプレビューと適用・
/// 元に戻す / やり直し・保存サイズの計算・保存が最後まで通り、結果が正しいことを確かめる。
/// 時間は PC の負荷で変わるので判定せず、テストの出力に書く（実装計画書 13-5）
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T18LargeImageTests(ITestOutputHelper output) : UiTestBase(output)
{
    [UiFact]
    [Trait("No", "T18")]
    public void T18_LargeImage_OpenZoomFilterUndoExport()
    {
        var watch = Stopwatch.StartNew();
        var app = LaunchWebDeza(UiTestEnvironment.Image("large", "large_8000x6000.jpg"));
        bool opened = Waits.Until(() => app.DocumentTitles().Count == 1 && app.Status().Contains("画像: 8000×6000"), TimeSpan.FromSeconds(120));
        Check.Note($"開く（起動を含む）: {watch.Elapsed.TotalSeconds:F1} 秒");
        Check.That("T18 8000×6000 の画像を開く", opened, app.Status());
        Thread.Sleep(800);

        // ズームとパン
        var center = app.CanvasPoint(0.5, 0.5);
        int fit = app.ZoomPercent();
        var times = new List<string>();
        int Zoom(string what, Action action)
        {
            int before = app.ZoomPercent();
            var t = Stopwatch.StartNew();
            action();
            Waits.Until(() => app.ZoomPercent() != before, TimeSpan.FromSeconds(10));
            int after = app.ZoomPercent();
            times.Add($"{what} {before}%→{after}% {t.Elapsed.TotalSeconds:F2} 秒");
            return after;
        }
        int up1 = Zoom("ホイール↑", () => Pointer.Wheel(center, 1));
        int up2 = Zoom("ホイール↑", () => Pointer.Wheel(center, 1));
        int down = Zoom("ホイール↓", () => Pointer.Wheel(center, -1));
        int actual = Zoom("100%", () => app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1));
        app.Shortcut(VirtualKeyShort.KEY_H);
        var beforePan = CaptureAway(app);
        var pan = Stopwatch.StartNew();
        Pointer.Drag(center, new Point(center.X - 250, center.Y - 180), 20);
        times.Add($"パン {pan.Elapsed.TotalSeconds:F2} 秒");
        var afterPan = CaptureAway(app);
        double panDiff = Pixels.PixelDiff(beforePan, afterPan);
        int fitAgain = Zoom("全体表示", () => app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_0));
        app.Shortcut(VirtualKeyShort.KEY_M);
        Check.Note(string.Join(" ／ ", times));
        Check.That("T18 ホイールで拡大・縮小、100%、手のひらのパン、全体表示 → それぞれ表示が変わる",
            up1 > fit && up2 > up1 && down < up2 && actual == 100 && panDiff > 5 && fitAgain == fit,
            $"全体表示 {fit}% → {up1}% → {up2}% → {down}% → {actual}% → {fitAgain}% ／ パンの前後の差 {panDiff:F1}");

        // フィルターのプレビュー（全体表示では半径の違いが見分けにくいので、100% で見る）
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Waits.Until(() => app.ZoomPercent() == 100, TimeSpan.FromSeconds(10));
        var original = CaptureAway(app);
        app.Driver.Menu("フィルター", "ぼかし");
        var section = app.FilterSection();
        var radius10 = MeasureChange(app, original, () => WebDezaApp.SetSlider(app.FilterParameter("半径"), 10), 0.5, out double tBlur10);
        var radius20 = MeasureChange(app, radius10, () => WebDezaApp.SetSlider(app.FilterParameter("半径"), 20), 0.3, out double tBlur20);
        var filterCombo = AppDriver.ById(section, "FilterCombo") ?? throw new InvalidOperationException("フィルターの一覧が無い");
        app.Driver.SelectCombo(filterCombo, "基本補正");
        Thread.Sleep(500);
        var plain = CaptureAway(app);
        MeasureChange(app, plain, () => WebDezaApp.SetSlider(app.FilterParameter("明るさ"), 40), 1, out double tBrightness);
        AppDriver.Invoke(AppDriver.ById(section, "FilterResetButton") ?? throw new InvalidOperationException("［リセット］が無い"));
        Check.Note($"プレビュー: ぼかし 0→10 {tBlur10:F1} 秒・10→20 {tBlur20:F1} 秒 ／ 明るさ 0→40 {tBrightness:F1} 秒");
        Check.That("T18 ぼかしの半径・基本補正の明るさを動かす → プレビューが追いかけて変わる",
            tBlur10 >= 0 && tBlur20 >= 0 && tBrightness >= 0, $"変わらなかったものは -1 秒: {tBlur10:F1} / {tBlur20:F1} / {tBrightness:F1}");

        // 適用（画面が固まらないかは、ステータスバーを読むのにかかった時間で見る。判定しない）
        app.Driver.SelectCombo(filterCombo, "ぼかし");
        Thread.Sleep(500);
        WebDezaApp.SetSlider(app.FilterParameter("半径"), 20);
        Thread.Sleep(3000);
        var preview = CaptureAway(app);
        var apply = AppDriver.ById(section, "FilterApplyButton") ?? throw new InvalidOperationException("［適用］が無い");
        var applying = Stopwatch.StartNew();
        var invoke = Task.Run(() => AppDriver.InvokeNow(apply));
        double slowest = 0;
        while (applying.Elapsed < TimeSpan.FromSeconds(120))
        {
            var t = Stopwatch.StartNew();
            bool dirty = SafeIsDirty(app);
            app.Status();
            slowest = Math.Max(slowest, t.Elapsed.TotalSeconds);
            if (dirty) break;
            Thread.Sleep(100);
        }
        double tApply = applying.Elapsed.TotalSeconds;
        invoke.Wait(TimeSpan.FromSeconds(5));
        var applied = CaptureAway(app, 1500);
        SaveShot(applied, "T18_applied");
        double toPreview = Pixels.PixelDiff(preview, applied), toOriginal = Pixels.PixelDiff(original, applied);
        Check.Note($"ぼかし（半径 20）の適用: {tApply:F1} 秒 ／ その間にステータスバーを読むのにかかった時間のいちばん長いもの {slowest:F2} 秒");
        Check.That("T18 ぼかしを［適用］ → 終わり、* が付く。見た目はプレビューと同じで、元とは違う",
            app.IsDirty() && toPreview < 1.5 && toOriginal > 0.5, $"プレビューとの差 {toPreview:F2} ／ 元との差 {toOriginal:F2}");

        var undoing = Stopwatch.StartNew();
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        bool undone = Waits.Until(() => !SafeIsDirty(app), TimeSpan.FromSeconds(30));
        double tUndo = undoing.Elapsed.TotalSeconds;
        double undoDiff = Pixels.PixelDiff(original, CaptureAway(app));
        var redoing = Stopwatch.StartNew();
        app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Y);
        bool redone = Waits.Until(() => SafeIsDirty(app), TimeSpan.FromSeconds(30));
        double tRedo = redoing.Elapsed.TotalSeconds;
        double redoDiff = Pixels.PixelDiff(applied, CaptureAway(app));
        Check.Note($"元に戻す {tUndo:F2} 秒 ／ やり直し {tRedo:F2} 秒");
        Check.That("T18 Ctrl+Z → 元の絵に戻る。Ctrl+Y → ぼかした絵に戻る",
            undone && undoDiff < 0.5 && redone && redoDiff < 0.5, $"戻した後の元との差 {undoDiff:F2} ／ やり直した後の適用後との差 {redoDiff:F2}");

        // 保存サイズの計算と保存
        var computing = Stopwatch.StartNew();
        var dialog = ExportDialog.Open(app);
        bool ready = dialog.WaitSizeReady(TimeSpan.FromSeconds(120));
        double tSize = computing.Elapsed.TotalSeconds;
        string size85 = dialog.FileSizeText();
        var requality = Stopwatch.StartNew();
        WebDezaApp.SetSlider(dialog.Quality, 60);
        bool ready60 = dialog.WaitSizeReady(TimeSpan.FromSeconds(120));
        double tSize60 = requality.Elapsed.TotalSeconds;
        string size60 = dialog.FileSizeText();
        long shown = dialog.TooltipBytes();
        Check.Note($"保存サイズ: 開いてから {tSize:F1} 秒（{size85}） ／ 品質 60 にしてから {tSize60:F1} 秒（{size60}）");
        Check.That("T18 Ctrl+E → 保存サイズが出る。品質を 60 にすると変わる", ready && ready60 && size85 != size60 && shown > 0, $"{size85} → {size60}");

        var saving = Stopwatch.StartNew();
        string? file = dialog.SaveNewFile(timeout: TimeSpan.FromSeconds(120));
        Check.Note($"保存: {saving.Elapsed.TotalSeconds:F1} 秒");
        var savedSize = file is null ? Size.Empty : Pixels.ImageSize(file);
        long bytes = file is null ? -1 : new FileInfo(file).Length;
        Check.That("T18 保存する → 8000×6000 のファイルができ、大きさは保存サイズの表示（ツールチップのバイト数）と同じ",
            savedSize == new Size(8000, 6000) && bytes == shown, $"{Path.GetFileName(file ?? "（無い）")} ／ {savedSize.Width}×{savedSize.Height} ／ {bytes:N0} バイト（表示 {shown:N0}）");
        Check.AssertAll();
    }

    /// <summary>
    /// 操作してから、キャンバスが before と diff より大きく違うまで待ち、その絵を返す。
    /// かかった秒数を seconds に（30 秒待っても変わらなければ -1）
    /// </summary>
    private static Pixels MeasureChange(WebDezaApp app, Pixels before, Action action, double diff, out double seconds)
    {
        var watch = Stopwatch.StartNew();
        action();
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            var now = CaptureAway(app, 0);
            if (Pixels.PixelDiff(before, now) > diff)
            {
                seconds = watch.Elapsed.TotalSeconds;
                Thread.Sleep(1000);
                return CaptureAway(app, 0);
            }
        }
        seconds = -1;
        return CaptureAway(app, 0);
    }

    /// <summary>マウスをキャンバスの外（メインウィンドウのタイトルバーの中ほど）に動かしてから、キャンバスを取り込む</summary>
    private static Pixels CaptureAway(WebDezaApp app, int settleMilliseconds = 700)
    {
        var main = app.Main.BoundingRectangle;
        Pointer.MoveTo(new Point(main.X + main.Width / 2, main.Y + 15));
        return app.SettleAndCapture(settleMilliseconds);
    }

    private static bool SafeIsDirty(WebDezaApp app)
    {
        try { return app.IsDirty(); }
        catch (Exception ex) when (Waits.IsTransient(ex)) { return false; }
    }
}
