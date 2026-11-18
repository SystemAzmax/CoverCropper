using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T02 ファイルロック（設計書 6.4・6.5）。開いている画像のファイルを、名前の変更・削除ができる。削除した後も表示・編集・保存できる。
/// エクスプローラーでの操作の代わりに、テストから File.Move・File.Delete で行う（手で確かめるのはエクスプローラーでの操作だけ）
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T02FileLockTests(ITestOutputHelper output) : UiTestBase(output)
{
    [UiFact]
    [Trait("No", "T02")]
    public void T02_OpenFile_CanBeRenamedAndDeleted_AndStillEditedAndSaved()
    {
        string folder = FreshFolder("T02");
        string lockFile = Path.Combine(folder, "lock_test.jpg"), other = Path.Combine(folder, "other.jpg");
        File.Copy(UiTestEnvironment.Image("lock", "lock_test.jpg"), lockFile);
        File.Copy(UiTestEnvironment.Image("formats", "sample.jpg"), other);
        var app = LaunchWebDeza(lockFile, other);
        Waits.Until(() => app.DocumentTitles().Count == 2, TimeSpan.FromSeconds(30));
        app.ActivateDocument("lock_test.jpg");

        string renamed = Path.Combine(folder, "renamed.jpg");
        string? renameError = TryFileOperation(() => File.Move(lockFile, renamed));
        Check.That("T02 開いたまま名前を変える → 変えられる", renameError is null && File.Exists(renamed), renameError ?? "");
        string? deleteError = TryFileOperation(() => File.Delete(renamed));
        Check.That("T02 名前を変えたファイルを削除する → 削除できる", deleteError is null && !File.Exists(renamed), deleteError ?? "");

        var before = app.CaptureCanvas();
        app.Driver.Menu("画像", "左右反転");
        Thread.Sleep(500);
        var after = app.CaptureCanvas();
        SaveShot(before, "T02_before");
        SaveShot(after, "T02_mirrored");
        double asymmetry = Pixels.PixelDiff(before, before.MirrorHorizontal());
        double mirrored = Pixels.PixelDiff(after, before.MirrorHorizontal());
        string? saved = app.SaveWithCurrentSettings();
        double savedDiff = saved is null
            ? double.NaN
            : Pixels.ThumbDiff(Pixels.Load(saved), Pixels.Load(UiTestEnvironment.Image("lock", "lock_test.jpg")).MirrorHorizontal());
        Check.That("T02 削除した後も、表示・編集（左右反転）・保存できる。保存したファイルは、元の画像を左右反転した絵",
            asymmetry > 1 && mirrored < 2 && saved is not null && Path.GetFileName(saved).StartsWith("lock_test_") && savedDiff < 10,
            $"左右の違い {asymmetry:F1} ／ 反転後と期待の差 {mirrored:F1} ／ 保存 {Path.GetFileName(saved ?? "（無い）")} ／ 元の画像の反転との差 {savedDiff:F1}");
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T02")]
    public void T02_WhileExportDialogIsComputing_OtherOpenFileCanBeDeleted()
    {
        // 保存サイズの計算に時間がかかる大きな画像で保存のダイアログを開き、計算中に、開いている別の画像のファイルを消す
        string folder = FreshFolder("T02-export");
        string big = Path.Combine(folder, "big.jpg"), other = Path.Combine(folder, "other.jpg");
        File.Copy(UiTestEnvironment.Image("large", "large_8000x6000.jpg"), big);
        File.Copy(UiTestEnvironment.Image("formats", "sample.jpg"), other);
        var app = LaunchWebDeza(other, big);
        Waits.Until(() => app.DocumentTitles().Count == 2, TimeSpan.FromSeconds(60));
        app.ActivateDocument("big.jpg");

        var dialog = app.OpenExportDialog();
        var sizeText = AppDriver.ById(dialog, "FileSizeText") ?? throw new InvalidOperationException("保存サイズの表示が無い");
        bool computing = sizeText.Name.Contains("計算中");
        string? deleteError = TryFileOperation(() => File.Delete(other));
        bool stillComputing = sizeText.Name.Contains("計算中");
        Check.That("T02 保存のダイアログを開いている間（計算中も）に、開いている別の画像のファイルを削除する → 削除できる",
            computing && deleteError is null && !File.Exists(other),
            $"消す前に計算中 {computing} ／ 消した後も計算中 {stillComputing}{(deleteError is null ? "" : " ／ " + deleteError)}");

        AppDriver.ClickButton(dialog, "キャンセル");
        Check.That("T02 キャンセルで保存のダイアログが閉じる", Waits.Until(() => !app.Driver.IsOpen(dialog), TimeSpan.FromSeconds(10)));
        Check.AssertAll();
    }
}
