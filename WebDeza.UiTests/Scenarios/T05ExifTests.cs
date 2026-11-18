using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T05 EXIF（設計書 6.4・6.5）。exif\portrait_orientation6.jpg（画素は横向き 1200×900 で、EXIF の向きが 6）が、
/// キャンバスと保存したファイルで正しい向き（縦長で、黄色い矢印が上を向いて右上にある）
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T05ExifTests(ITestOutputHelper output) : UiTestBase(output)
{
    [UiFact]
    [Trait("No", "T05")]
    public void T05_Orientation6_CanvasAndSavedFileAreUpright()
    {
        var app = OpenInWebDeza(UiTestEnvironment.Image("exif", "portrait_orientation6.jpg"));
        Check.That("T05 縦長で開く（画像: 900×1200）", app.WaitStatus(s => s.Contains("画像: 900×1200"), TimeSpan.FromSeconds(5)), app.Status());

        var canvas = app.CaptureCanvas();
        SaveShot(canvas, "T05_canvas");
        var arrow = canvas.Arrow(out var detail);
        var center = canvas.YellowCenter();
        Check.That("T05 キャンバスで矢印が上を向き、右上にある（正しい向き）",
            arrow == ArrowDirection.Up && center is { X: > 0.5, Y: < 0.4 }, $"矢印 {arrow} {detail}");

        string? file = app.Save("JPEG");
        if (Check.That("T05 JPEG で保存できる", file is not null, app.Status()))
        {
            var saved = Pixels.Load(file!);
            var savedArrow = saved.Arrow(out var savedDetail);
            var savedCenter = saved.YellowCenter();
            int orientation = Pixels.ExifOrientation(file!);
            Check.That("T05 保存したファイル: 画素が 900×1200 で正しい向き（EXIF の向きに頼らない）",
                saved.Width == 900 && saved.Height == 1200 && savedArrow == ArrowDirection.Up && savedCenter is { X: > 0.7, Y: < 0.3 },
                $"{saved.Width}×{saved.Height} ／ 矢印 {savedArrow} {savedDetail}");
            Check.That("T05 保存したファイル: EXIF の向きが無い（1）ので、どのビューアーでも同じ向き", orientation == 1, "Orientation=" + orientation);
        }
        Check.AssertAll();
    }
}
