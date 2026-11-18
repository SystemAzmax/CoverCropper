using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>T04 形式（設計書 6.4・6.5）。テスト画像を作れる形式が、正しい大きさと向きで読める</summary>
[Collection(UiCollection.Name)]
public sealed class T04FormatTests(ITestOutputHelper output) : UiTestBase(output)
{
    [UiFact]
    [Trait("No", "T04")]
    public void T04_OpensEachFormat_WithSameSizeAndPicture()
    {
        // 1200×900 の同じ絵を、形式ごとに保存したもの。HEIC は、作れた環境だけ（WebP・AVIF は作れないので手で確かめる）
        string[] samePicture = UiSession.HasHeic
            ? ["sample.jpg", "sample.bmp", "sample.tif", "sample.gif", "sample.jxr", "sample.heic"]
            : ["sample.jpg", "sample.bmp", "sample.tif", "sample.gif", "sample.jxr"];
        string[] names = ["sample.png", .. samePicture, "sample.ico", "png_content.jpg"];
        Check.Note("HEIC: " + (UiSession.HasHeic ? "あり" : "テスト画像を作れないので確かめない"));

        var app = LaunchWebDeza(names.Select(n => UiTestEnvironment.Image("formats", n)).ToArray());
        app.WaitStatus(s => s.Contains("件を開きました"), TimeSpan.FromSeconds(30));
        Thread.Sleep(1000);
        Check.That("T04 すべての形式を開いても落ちない", !app.Driver.HasExited);
        var titles = app.DocumentTitles();
        Check.That($"T04 {names.Length} 件が開く", titles.Count == names.Length, string.Join(", ", titles) + " ／ " + app.Status());

        app.ActivateDocument("sample.png");
        var reference = app.CaptureCanvas();
        SaveShot(reference, "T04_png");
        Check.That("T04 PNG の絵の矢印が上を向く（基準の絵が正しい）", reference.Arrow(out var referenceDetail) == ArrowDirection.Up, referenceDetail);

        foreach (var name in samePicture)
        {
            app.ActivateDocument(name);
            bool size = app.WaitStatus(s => s.Contains("画像: 1200×900"), TimeSpan.FromSeconds(3));
            var shot = app.CaptureCanvas();
            SaveShot(shot, "T04_" + Path.GetExtension(name).TrimStart('.'));
            double diff = Pixels.ThumbDiff(reference, shot);
            Check.That($"T04 {name}: 1200×900 で、PNG と同じ絵", size && diff < 10, $"{app.Status()} ／ PNG との差 {diff:F1}");
        }

        app.ActivateDocument("sample.ico");
        Check.That("T04 sample.ico: いちばん大きい 256×256", app.WaitStatus(s => s.Contains("画像: 256×256"), TimeSpan.FromSeconds(3)), app.Status());
        app.ActivateDocument("png_content.jpg");
        Check.That("T04 png_content.jpg（中身は PNG）: 開ける（800×600）", app.WaitStatus(s => s.Contains("画像: 800×600"), TimeSpan.FromSeconds(3)), app.Status());
        Check.That("T04 最後まで落ちない", !app.Driver.HasExited);
        Check.AssertAll();
    }
}
