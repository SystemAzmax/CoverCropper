using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>テスト画像のフォルダ（手動テスト手順の「準備」の表と同じ）</summary>
[Flags]
public enum ImageSet
{
    None = 0,
    /// <summary>formats: 5.4 の形式（1200×900）、ICO、中身が PNG の .jpg、テキスト。WebP・HEIC はエンコーダーがあるときだけ</summary>
    Formats = 1 << 0,
    /// <summary>exif: EXIF の向きが 6 の縦撮りの写真</summary>
    Exif = 1 << 1,
    /// <summary>transparent: 透過 PNG</summary>
    Transparent = 1 << 2,
    /// <summary>many: 60 枚</summary>
    Many = 1 << 3,
    /// <summary>batch: 4000×3000 の 10 枚（約 6 MB ずつ）</summary>
    Batch = 1 << 4,
    /// <summary>large: 8000×6000</summary>
    Large = 1 << 5,
    /// <summary>mixed: 対応 3 件・非対応 2 件・サブフォルダ</summary>
    Mixed = 1 << 6,
    /// <summary>noimages: 対応する画像が無いフォルダ</summary>
    NoImages = 1 << 7,
    /// <summary>broken: 中身が画像でない 2 件と、正しい 1 件</summary>
    Broken = 1 << 8,
    /// <summary>lock: ファイルロックの確認用</summary>
    Lock = 1 << 9,
    All = (1 << 10) - 1,
}

/// <summary>
/// テスト画像を作る（受け入れ確認 12-2 で scratchpad に作ったプログラムを移したもの）。
/// 絵は乱数の種を決めて作るので、毎回同じになる。WPF の描画と WIC は STA のスレッドで使う
/// </summary>
public static class TestImageFactory
{
    /// <summary>画像を作るコードの版。絵やファイルを変えたら上げる（TestImageCache が作り直す）</summary>
    public const string Version = "1";

    private static readonly Guid WebpContainer = new("E094B0E2-67F2-45B3-B0EA-115337CA7CF3");
    private static readonly Guid HeifContainer = new("E1E62521-6787-405B-A339-500715B5763F");

    public static void Generate(string root, ImageSet sets) => StaThread.Run(() => GenerateCore(root, sets));

    private static void GenerateCore(string root, ImageSet sets)
    {
        Directory.CreateDirectory(root);

        if (sets.HasFlag(ImageSet.Formats))
        {
            string formats = Dir(root, "formats");
            var sample = Photo(1200, 900, 1, "形式テスト");
            Save(new JpegBitmapEncoder { QualityLevel = 90 }, sample, Path.Combine(formats, "sample.jpg"));
            Save(new PngBitmapEncoder(), sample, Path.Combine(formats, "sample.png"));
            Save(new BmpBitmapEncoder(), sample, Path.Combine(formats, "sample.bmp"));
            Save(new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw }, sample, Path.Combine(formats, "sample.tif"));
            Save(new GifBitmapEncoder(), sample, Path.Combine(formats, "sample.gif"));
            Save(new WmpBitmapEncoder(), sample, Path.Combine(formats, "sample.jxr"));
            WriteIco(Path.Combine(formats, "sample.ico"), Photo(256, 256, 2, "ICO 256"), Photo(32, 32, 3, ""));
            TrySave(WebpContainer, sample, Path.Combine(formats, "sample.webp"));
            TrySave(HeifContainer, sample, Path.Combine(formats, "sample.heic"));
            // 拡張子が .jpg で中身が PNG
            Save(new PngBitmapEncoder(), Photo(800, 600, 4, "中身は PNG"), Path.Combine(formats, "png_content.jpg"));
            File.WriteAllText(Path.Combine(formats, "not_image.txt"), "画像ではないファイル（非対応の確認用）\n");
        }

        if (sets.HasFlag(ImageSet.Exif))
        {
            // EXIF の向き 6（時計回りに 90° 回して見る）。保存する画素は横向き
            var portrait = Photo(900, 1200, 5, "上（縦撮り）");
            var stored = new TransformedBitmap(portrait, new RotateTransform(270));
            var metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
            var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(stored, null, metadata, null));
            using var fs = File.Create(Path.Combine(Dir(root, "exif"), "portrait_orientation6.jpg"));
            encoder.Save(fs);
        }

        if (sets.HasFlag(ImageSet.Transparent))
            Save(new PngBitmapEncoder(), Transparent(800, 600), Path.Combine(Dir(root, "transparent"), "transparent.png"));

        if (sets.HasFlag(ImageSet.Many))
        {
            // 50 枚の上限と、名前の自然順の確認
            string many = Dir(root, "many");
            for (int i = 1; i <= 60; i++)
                Save(new JpegBitmapEncoder { QualityLevel = 85 }, Photo(640, 480, 100 + i, $"No.{i}"), Path.Combine(many, $"img_{i}.jpg"));
        }

        if (sets.HasFlag(ImageSet.Batch))
        {
            // 一括保存の 10 枚（ノイズが多く、エンコードに少し時間がかかる）
            string batch = Dir(root, "batch");
            for (int i = 1; i <= 10; i++)
                Save(new JpegBitmapEncoder { QualityLevel = 92 }, Photo(4000, 3000, 200 + i, $"一括 {i}", noise: 40), Path.Combine(batch, $"photo_{i:00}.jpg"));
        }

        if (sets.HasFlag(ImageSet.Large))
            Save(new JpegBitmapEncoder { QualityLevel = 90 }, Photo(8000, 6000, 300, "8000×6000", noise: 24), Path.Combine(Dir(root, "large"), "large_8000x6000.jpg"));

        if (sets.HasFlag(ImageSet.Mixed))
        {
            string mixed = Dir(root, "mixed");
            Save(new JpegBitmapEncoder(), Photo(800, 600, 400, "mixed a"), Path.Combine(mixed, "a.jpg"));
            Save(new PngBitmapEncoder(), Photo(800, 600, 401, "mixed b"), Path.Combine(mixed, "b.png"));
            Save(new BmpBitmapEncoder(), Photo(800, 600, 402, "mixed c"), Path.Combine(mixed, "c.bmp"));
            File.WriteAllText(Path.Combine(mixed, "readme.txt"), "非対応\n");
            File.WriteAllText(Path.Combine(mixed, "data.csv"), "a,b\n");
            Save(new JpegBitmapEncoder(), Photo(800, 600, 403, "サブフォルダ（開かない）"), Path.Combine(Dir(mixed, "sub"), "d.jpg"));
        }

        if (sets.HasFlag(ImageSet.NoImages))
            File.WriteAllText(Path.Combine(Dir(root, "noimages"), "readme.txt"), "画像はありません\n");

        if (sets.HasFlag(ImageSet.Broken))
        {
            string broken = Dir(root, "broken");
            File.WriteAllText(Path.Combine(broken, "broken1.jpg"), "not a jpeg\n");
            File.WriteAllText(Path.Combine(broken, "broken2.png"), "not a png\n");
            Save(new JpegBitmapEncoder(), Photo(800, 600, 500, "ok"), Path.Combine(broken, "ok.jpg"));
        }

        if (sets.HasFlag(ImageSet.Lock))
            Save(new JpegBitmapEncoder(), Photo(800, 600, 600, "lock"), Path.Combine(Dir(root, "lock"), "lock_test.jpg"));
    }

    private static string Dir(string parent, string name)
    {
        string path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Save(BitmapEncoder encoder, BitmapSource source, string path)
    {
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>OS にエンコーダーがあるときだけ作る（WebP・HEIF。無ければファイルを作らない）</summary>
    private static void TrySave(Guid container, BitmapSource source, string path)
    {
        try
        {
            Save(BitmapEncoder.Create(container), new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0), path);
        }
        catch (Exception)
        {
            // エンコーダーが無いときの例外は環境で違う（NotSupportedException・COMException など）。
            // GitHub Actions のランナーのように、エンコーダーはあっても HEVC の拡張機能が無く、保存で失敗する環境もある
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>写真らしい画像（グラデーション・ノイズ・図形・文字・上を指す黄色い矢印）</summary>
    private static BitmapSource Photo(int width, int height, int seed, string label, int noise = 12)
    {
        var random = new Random(seed);
        int stride = width * 4;
        var pixels = new byte[stride * height];
        double hue = random.NextDouble();
        for (int y = 0; y < height; y++)
        {
            double fy = (double)y / height;
            for (int x = 0; x < width; x++)
            {
                double fx = (double)x / width;
                int i = y * stride + x * 4;
                int n = noise == 0 ? 0 : random.Next(-noise, noise + 1);
                pixels[i + 0] = Clamp(80 + 150 * fx * (0.5 + hue) + n);
                pixels[i + 1] = Clamp(60 + 160 * fy + n);
                pixels[i + 2] = Clamp(200 - 120 * fx * fy + 40 * Math.Sin(fx * 12 + hue * 6) + n);
                pixels[i + 3] = 255;
            }
        }
        var background = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(background, new Rect(0, 0, width, height));
            double unit = Math.Min(width, height);
            for (int k = 0; k < 6; k++)
            {
                var color = Color.FromRgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                dc.DrawEllipse(new SolidColorBrush(color), new Pen(Brushes.White, unit / 200 + 1),
                    new WpfPoint(random.NextDouble() * width, random.NextDouble() * height), unit * 0.08, unit * 0.08);
            }
            // 細かい模様（ぼかし・ブロックノイズが見やすい）
            var pen = new Pen(Brushes.Black, Math.Max(1, unit / 400));
            for (double x = width * 0.05; x < width * 0.3; x += Math.Max(3, unit / 100))
                dc.DrawLine(pen, new WpfPoint(x, height * 0.7), new WpfPoint(x, height * 0.95));
            if (label.Length > 0)
            {
                var text = new FormattedText(label, CultureInfo.GetCultureInfo("ja-JP"), System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Yu Gothic UI"), unit / 8, Brushes.White, 1.0);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), null,
                    new Rect(width * 0.05 - 8, height * 0.08 - 4, text.Width + 16, text.Height + 8));
                dc.DrawText(text, new WpfPoint(width * 0.05, height * 0.08));
                // 上を指す矢印（向きの確認用。Pixels.Arrow で向きを調べる）
                var arrow = new StreamGeometry();
                using (var g = arrow.Open())
                {
                    double cx = width * 0.85, top = height * 0.08, size = unit * 0.12;
                    g.BeginFigure(new WpfPoint(cx, top), true, true);
                    g.LineTo(new WpfPoint(cx + size / 2, top + size), true, false);
                    g.LineTo(new WpfPoint(cx - size / 2, top + size), true, false);
                }
                dc.DrawGeometry(Brushes.Yellow, new Pen(Brushes.Black, 2), arrow);
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
    }

    /// <summary>透過のある画像（透明な背景・不透明な赤い円・半透明の青い矩形・右へ透明になる緑の帯）</summary>
    private static BitmapSource Transparent(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(Brushes.Red, null, new WpfPoint(width * 0.3, height * 0.4), height * 0.25, height * 0.25);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(128, 0, 0, 255)), null,
                new Rect(width * 0.4, height * 0.2, width * 0.4, height * 0.4));
            var gradient = new LinearGradientBrush(Color.FromArgb(255, 0, 160, 0), Color.FromArgb(0, 0, 160, 0), 0);
            dc.DrawRectangle(gradient, null, new Rect(width * 0.1, height * 0.75, width * 0.8, height * 0.15));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
    }

    /// <summary>PNG を埋め込んだ ICO（大きさの違う 2 枚）</summary>
    private static void WriteIco(string path, params BitmapSource[] images)
    {
        var pngs = images.Select(image =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return (image.PixelWidth, image.PixelHeight, Data: ms.ToArray());
        }).ToList();

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)pngs.Count);
        int offset = 6 + 16 * pngs.Count;
        foreach (var png in pngs)
        {
            w.Write((byte)(png.PixelWidth >= 256 ? 0 : png.PixelWidth));
            w.Write((byte)(png.PixelHeight >= 256 ? 0 : png.PixelHeight));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(png.Data.Length);
            w.Write(offset);
            offset += png.Data.Length;
        }
        foreach (var png in pngs) w.Write(png.Data);
    }

    private static byte Clamp(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
}
