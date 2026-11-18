using FlaUI.Core.WindowsAPI;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T06 透過（設計書 6.4・6.5）。transparent\transparent.png（800×600。透明な背景・不透明な赤い円・
/// 半透明の青い矩形 (0.4〜0.8, 0.2〜0.6)・右へ透明になる緑の帯 (0.1〜0.9, 0.75〜0.9)）を使う
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T06TransparencyTests(ITestOutputHelper output) : UiTestBase(output)
{
    private static string Transparent => UiTestEnvironment.Image("transparent", "transparent.png");

    [UiFact]
    [Trait("No", "T06")]
    public void T06_CanvasShowsCheckerAndPngTiffKeepTransparency()
    {
        var app = OpenInWebDeza(Transparent);
        var canvas = app.CaptureCanvas();
        SaveShot(canvas, "T06_canvas");
        var box = ImageBox(canvas);
        bool corner = Checker(Patch(canvas, box, 0.02, 0.12, 0.03, 0.15), out var cornerDetail);
        var bluePatch = Patch(canvas, box, 0.6, 0.78, 0.25, 0.55);
        bool blue = Checker(bluePatch, out var blueDetail) && BlueOverRed(bluePatch) > 40;
        bool green = Checker(Patch(canvas, box, 0.82, 0.88, 0.77, 0.88), out var greenDetail);
        Check.That("T06 開く → 透明な所に市松模様。青い矩形の下と緑の帯の右側も、市松模様が透けて見える",
            corner && blue && green, $"左上 {cornerDetail} ／ 青 {blueDetail} ／ 緑の右 {greenDetail} ／ 画像の範囲 {box}");

        var dialog = ExportDialog.Open(app);
        dialog.SelectFormat("PNG");
        string notice = dialog.Text("AlphaNoticeText");
        string? png = dialog.SaveNewFile();
        int pngAlpha = png is null ? 0 : Pixels.Load(png).TransparentCount();
        bool reopened = false;
        string reopenedDetail = "";
        if (png is not null)
        {
            app.Driver.OpenViaDialog(Path.GetDirectoryName(png)!, Path.GetFileName(png));
            Waits.Until(() => app.DocumentTitles().Count == 2 && app.Title().StartsWith(Path.GetFileName(png)), TimeSpan.FromSeconds(10));
            Thread.Sleep(500);
            var again = app.CaptureCanvas();
            reopened = app.Title().StartsWith(Path.GetFileName(png)) && Checker(Patch(again, ImageBox(again), 0.02, 0.12, 0.03, 0.15), out reopenedDetail);
            app.Shortcut(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
            Waits.Until(() => app.DocumentTitles().Count == 1, TimeSpan.FromSeconds(5));
        }
        Check.That("T06 PNG で保存 → M24 は出ない。開き直すと透過が残っている（市松模様）",
            notice.Length == 0 && pngAlpha > 1000 && reopened,
            $"M24 「{notice}」 ／ 透過の画素 {pngAlpha} ／ 開き直し {reopenedDetail}");

        string? tif = app.Save("TIFF");
        int tifAlpha = tif is null ? 0 : Pixels.Load(tif).TransparentCount();
        Check.That("T06 TIFF で保存 → 透過が残る", tif is not null && Path.GetExtension(tif) == ".tif" && tifAlpha > 1000,
            $"{Path.GetFileName(tif ?? "（無い）")} ／ 透過の画素 {tifAlpha}");
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T06")]
    public void T06_JpegAndBmp_ShowM24AndFillWithBackground()
    {
        var app = OpenInWebDeza(Transparent);
        var dialog = ExportDialog.Open(app);
        dialog.SelectFormat("JPEG");
        string jpegNotice = dialog.Text("AlphaNoticeText");
        dialog.SelectFormat("BMP");
        string bmpNotice = dialog.Text("AlphaNoticeText");
        Check.That("T06 JPEG を選ぶ → M24「JPEG は透過を保てないため、透過部分を背景色で塗ります。」。BMP も同じ",
            jpegNotice == "JPEG は透過を保てないため、透過部分を背景色で塗ります。" && bmpNotice == "BMP は透過を保てないため、透過部分を背景色で塗ります。",
            $"{jpegNotice} ／ {bmpNotice}");

        // 背景色を変えると、圧縮前・圧縮後のプレビューの背景（いちばん多い色）がその色になる
        dialog.SelectFormat("JPEG");
        AppDriver.Invoke(dialog.Element("PreviewFitButton"));
        dialog.ClickSwatch("#000000");
        dialog.WaitSizeReady();
        Thread.Sleep(800);
        var (left, right) = dialog.PreviewPanes();
        var blackLeft = Dominant(Pixels.CaptureScreen(left));
        var blackRight = Dominant(Pixels.CaptureScreen(right));
        dialog.EnterBackground("#FF8800");
        dialog.WaitSizeReady();
        Thread.Sleep(800);
        SaveShot(Pixels.CaptureScreen(dialog.Window.BoundingRectangle), "T06_export_orange");
        var orangeLeft = Dominant(Pixels.CaptureScreen(left));
        var orangeRight = Dominant(Pixels.CaptureScreen(right));
        static bool IsBlack((int R, int G, int B, double Share) c) => c.R < 30 && c.G < 30 && c.B < 30;
        static bool IsOrange((int R, int G, int B, double Share) c) => c.R > 230 && c.G is > 115 and < 155 && c.B < 30;
        Check.That("T06 背景色を黒の色見本、#FF8800 の入力に変える → 圧縮前・圧縮後の背景がその色になる",
            IsBlack(blackLeft) && IsBlack(blackRight) && IsOrange(orangeLeft) && IsOrange(orangeRight),
            $"黒: 左 {blackLeft} 右 {blackRight} ／ 橙: 左 {orangeLeft} 右 {orangeRight}");

        dialog.ClickSwatch("#FFFFFF");
        string? jpg = dialog.SaveNewFile();
        if (Check.That("T06 JPEG・背景色 白で保存できる", jpg is not null, app.Status()))
        {
            var saved = Pixels.Load(jpg!);
            var transparent = saved.At(20, 20);
            var blue = saved.At((int)(saved.Width * 0.7), (int)(saved.Height * 0.3));
            Check.That("T06 JPEG・背景色 白で保存 → 透明な所が白、半透明の青い矩形は薄い青",
                transparent is { R: > 245, G: > 245, B: > 245 } && blue is { B: > 230, R: > 105 and < 150, G: > 105 and < 150 },
                $"透明な所 ({transparent.R},{transparent.G},{transparent.B}) ／ 青 ({blue.R},{blue.G},{blue.B})");
        }
        Check.AssertAll();
    }

    /// <summary>取り込んだキャンバスの中の、画像の範囲（左上の背景の色と違う画素を囲む矩形）</summary>
    private static Rectangle ImageBox(Pixels canvas)
    {
        var background = canvas.At(2, 2);
        int minX = canvas.Width, minY = canvas.Height, maxX = 0, maxY = 0;
        for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
            {
                var p = canvas.At(x, y);
                if (Math.Abs(p.R - background.R) + Math.Abs(p.G - background.G) + Math.Abs(p.B - background.B) <= 40) continue;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    /// <summary>画像の範囲の中の、割合 (fx0〜fx1, fy0〜fy1) の部分</summary>
    private static Pixels Patch(Pixels canvas, Rectangle box, double fx0, double fx1, double fy0, double fy1)
    {
        int x0 = box.X + (int)(box.Width * fx0), y0 = box.Y + (int)(box.Height * fy0);
        return canvas.Crop(x0, y0, box.X + (int)(box.Width * fx1) - x0, box.Y + (int)(box.Height * fy1) - y0);
    }

    /// <summary>市松模様が見えるか: 明るさで 2 つの群に分かれ、どちらも 15% 以上ある</summary>
    private static bool Checker(Pixels patch, out string detail)
    {
        var luminance = new List<int>();
        for (int y = 0; y < patch.Height; y++)
            for (int x = 0; x < patch.Width; x++)
            {
                var p = patch.At(x, y);
                luminance.Add((p.R * 3 + p.G * 6 + p.B) / 10);
            }
        if (luminance.Count == 0)
        {
            detail = "範囲が空";
            return false;
        }
        luminance.Sort();
        int low = luminance[luminance.Count / 10], high = luminance[luminance.Count * 9 / 10], middle = (low + high) / 2;
        double darkShare = luminance.Count(v => v < middle) / (double)luminance.Count;
        detail = $"明るさ {low}〜{high}、暗い側 {darkShare:P0}";
        return high - low >= 8 && darkShare is > 0.15 and < 0.85;
    }

    /// <summary>青が赤よりどれだけ強いか（平均）</summary>
    private static double BlueOverRed(Pixels patch)
    {
        double sum = 0;
        for (int i = 0; i < patch.Bgra.Length; i += 4) sum += patch.Bgra[i] - patch.Bgra[i + 2];
        return sum / Math.Max(1, patch.Bgra.Length / 4);
    }

    /// <summary>いちばん多い色（16 段階に丸めて数える）。プレビューの枠の暗い灰色（#2B2B2B 前後）は数えない</summary>
    private static (int R, int G, int B, double Share) Dominant(Pixels pixels)
    {
        var counts = new Dictionary<int, int>();
        for (int i = 0; i < pixels.Bgra.Length; i += 4)
        {
            byte b = pixels.Bgra[i], g = pixels.Bgra[i + 1], r = pixels.Bgra[i + 2];
            if (Math.Abs(b - g) < 4 && Math.Abs(g - r) < 4 && b is >= 34 and <= 52) continue;
            int key = (r / 16) << 8 | (g / 16) << 4 | (b / 16);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        if (counts.Count == 0) return (-1, -1, -1, 0);
        var top = counts.MaxBy(kv => kv.Value);
        return ((top.Key >> 8) * 16 + 8, ((top.Key >> 4) & 15) * 16 + 8, (top.Key & 15) * 16 + 8, (double)top.Value / (pixels.Bgra.Length / 4));
    }
}
