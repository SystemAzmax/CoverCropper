using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconGenerator;

/// <summary>
/// CoverCropper のアプリアイコンを描画し、マルチサイズ ICO（PNG 圧縮）とプレビュー画像を出力する。
/// デザイン: 風景写真の上に 4:3 の切り抜き枠。枠外は暗く、四隅に白い L 字ハンドル。
/// 座標は 256×256 の設計空間で記述し、小さいサイズでは枠をピクセル境界に合わせた専用レイアウトを使う。
/// </summary>
internal static class Program
{
    private const double Design = 256;
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        Directory.CreateDirectory(outDir);

        var images = Sizes.Select(s => (Size: s, Bitmap: Render(s))).ToList();

        WriteIco(Path.Combine(outDir, "AppIcon.ico"), images.Select(i => (i.Size, EncodePng(i.Bitmap))).ToList());
        File.WriteAllBytes(Path.Combine(outDir, "AppIcon_256.png"), EncodePng(images[^1].Bitmap));

        var previewPath = args.Length > 1 ? args[1] : Path.Combine(outDir, "AppIcon_preview.png");
        File.WriteAllBytes(previewPath, EncodePng(RenderPreview(images)));

        Console.WriteLine($"Wrote {Path.Combine(outDir, "AppIcon.ico")}");
        return 0;
    }

    // ------------------------------------------------------------------ 描画

    private static BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / Design, size / Design));
            Draw(dc, size);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Draw(DrawingContext dc, int size)
    {
        bool small = size <= 32;
        double px = Design / size; // 1 ピクセルの設計空間での大きさ

        // 背景（角丸の写真）
        var tile = small ? new Rect(0, 0, Design, Design) : new Rect(10, 10, 236, 236);
        double radius = small ? 3 * px : 46;
        var tileGeometry = new RectangleGeometry(tile, radius, radius);
        dc.PushClip(tileGeometry);

        // 空
        dc.DrawRectangle(new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromRgb(0x4F, 0xA8, 0xEE), 0.0),
                new(Color.FromRgb(0x9F, 0xD4, 0xF7), 0.55),
                new(Color.FromRgb(0xFF, 0xE2, 0xA8), 0.85),
            }, new Point(0, 0), new Point(0, 1)), null, tile);

        // 太陽
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x3C)), null, new Point(92, 102), 18, 18);

        // 遠景の山
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x4A, 0x78, 0xB5)), null, Polygon(
            (0, 178), (64, 112), (104, 150), (160, 96), (256, 176), (256, 256), (0, 256)));

        // 手前の丘
        var hill = new StreamGeometry();
        using (var g = hill.Open())
        {
            g.BeginFigure(new Point(0, 182), true, true);
            g.QuadraticBezierTo(new Point(90, 138), new Point(160, 174), true, true);
            g.QuadraticBezierTo(new Point(210, 198), new Point(256, 166), true, true);
            g.LineTo(new Point(256, 256), true, true);
            g.LineTo(new Point(0, 256), true, true);
        }
        dc.DrawGeometry(new LinearGradientBrush(
            Color.FromRgb(0x43, 0xB0, 0x6C), Color.FromRgb(0x23, 0x7A, 0x48), 90), null, hill);

        // 切り抜き枠（4:3）
        Rect crop;
        double thickness, arm;
        if (small)
        {
            // ピクセル境界に揃えたレイアウト（x, y, w, h, 線幅 をピクセル単位で指定）
            var (x, y, w, h, t) = size switch
            {
                16 => (2, 4, 12, 9, 1),
                20 => (2, 5, 16, 12, 2),
                24 => (2, 6, 20, 15, 2),
                _ => (4, 8, 24, 18, 2),
            };
            crop = new Rect(x * px, y * px, w * px, h * px);
            thickness = t * px;
            arm = Math.Round(w * 0.3) * px;
        }
        else
        {
            crop = new Rect(44, 70, 168, 126);
            thickness = 11;
            arm = 42;
        }

        // 枠外を暗く
        var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dim.Children.Add(new RectangleGeometry(tile));
        dim.Children.Add(new RectangleGeometry(crop));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0xA0, 0x0A, 0x14, 0x28)), null, dim);

        if (!small)
        {
            // 枠線と三分割ガイド
            var thin = new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), 3);
            dc.DrawRectangle(null, thin, crop);
            if (size >= 64)
            {
                var guide = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 2.5);
                for (int i = 1; i <= 2; i++)
                {
                    double gx = crop.X + crop.Width * i / 3, gy = crop.Y + crop.Height * i / 3;
                    dc.DrawLine(guide, new Point(gx, crop.Top), new Point(gx, crop.Bottom));
                    dc.DrawLine(guide, new Point(crop.Left, gy), new Point(crop.Right, gy));
                }
            }
        }

        // 四隅の L 字ハンドル（塗りの矩形で描き、小サイズでもにじまないようにする）
        var white = Brushes.White;
        foreach (var (cx, cy, sx, sy) in new[]
                 {
                     (crop.Left, crop.Top, 1, 1), (crop.Right, crop.Top, -1, 1),
                     (crop.Left, crop.Bottom, 1, -1), (crop.Right, crop.Bottom, -1, -1),
                 })
        {
            // 大サイズではハンドルを枠の外側に少しはみ出させる
            double o = small ? 0 : thickness / 2;
            double x0 = cx - sx * o, y0 = cy - sy * o;
            dc.DrawRectangle(white, null, Normalize(x0, y0, sx * arm, sy * thickness));
            dc.DrawRectangle(white, null, Normalize(x0, y0, sx * thickness, sy * arm));
        }

        dc.Pop();

        if (!small)
        {
            // タイルの縁取り（明るい背景でも輪郭が分かるように）
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x20, 0x50)), 2), tileGeometry);
        }
    }

    private static Rect Normalize(double x, double y, double w, double h)
        => new(Math.Min(x, x + w), Math.Min(y, y + h), Math.Abs(w), Math.Abs(h));

    private static Geometry Polygon(params (double X, double Y)[] points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(points[0].X, points[0].Y), true, true);
            g.PolyLineTo(points.Skip(1).Select(p => new Point(p.X, p.Y)).ToList(), true, true);
        }
        return geometry;
    }

    // ------------------------------------------------------------------ 出力

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>PNG 圧縮エントリのマルチサイズ ICO を書き出す（Windows Vista 以降対応）</summary>
    private static void WriteIco(string path, IReadOnlyList<(int Size, byte[] Png)> entries)
    {
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);

        w.Write((ushort)0);             // reserved
        w.Write((ushort)1);             // type: icon
        w.Write((ushort)entries.Count); // count

        int offset = 6 + 16 * entries.Count;
        foreach (var (size, png) in entries)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); // width
            w.Write((byte)(size >= 256 ? 0 : size)); // height
            w.Write((byte)0);   // palette
            w.Write((byte)0);   // reserved
            w.Write((ushort)1); // planes
            w.Write((ushort)32);// bpp
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in entries) w.Write(png);
    }

    /// <summary>明暗の背景に全サイズを並べ、小サイズは等倍と拡大の両方を表示する確認用画像</summary>
    private static BitmapSource RenderPreview(IReadOnlyList<(int Size, BitmapSource Bitmap)> images)
    {
        const int pad = 16;
        int width = pad + images.Sum(i => i.Size + pad);
        int zoomRow = 32 * 4 + pad * 2;
        int bandHeight = 256 + pad * 2 + zoomRow;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var backgrounds = new[] { Color.FromRgb(0xF3, 0xF3, 0xF3), Color.FromRgb(0x20, 0x20, 0x20) };
            for (int band = 0; band < backgrounds.Length; band++)
            {
                double top = band * bandHeight;
                dc.DrawRectangle(new SolidColorBrush(backgrounds[band]), null, new Rect(0, top, width, bandHeight));

                double x = pad;
                foreach (var (size, bitmap) in images)
                {
                    dc.DrawImage(bitmap, new Rect(x, top + pad + 256 - size, size, size));
                    x += size + pad;
                }

                // 小サイズを 4 倍（ニアレストネイバー）で表示
                x = pad;
                foreach (var (size, bitmap) in images.Where(i => i.Size <= 32))
                {
                    var zoomed = new DrawingGroup();
                    RenderOptions.SetBitmapScalingMode(zoomed, BitmapScalingMode.NearestNeighbor);
                    zoomed.Children.Add(new ImageDrawing(bitmap, new Rect(x, top + 256 + pad * 2, size * 4, size * 4)));
                    dc.DrawDrawing(zoomed);
                    x += size * 4 + pad;
                }
            }
        }
        var result = new RenderTargetBitmap(width, bandHeight * 2, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        return result;
    }
}
