using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoverCropper;
using CoverCropper.Services;

namespace BaselineGenerator;

/// <summary>
/// CoverCropper の画像処理を共通エンジンへ置き換える前に、今の処理結果を基準ファイルとして保存する。
/// 入力画像（inputs/）を決まった模様で作り、CoverCropper と同じ順番（読み込み → リサイズ → 中央で切り抜き → JPEG 品質 85）で
/// 処理した途中結果と出力を expected/ に書く。置き換えた後は CoverCropper.Tests の BaselineRegressionTests がこれと比べる。
/// 入力は可逆の形式（PNG / TIFF / GIF）だけにする。JPEG の入力は OS のコーデックで展開結果が変わりうるため使わない。
/// </summary>
internal static class Program
{
    private sealed record InputCase(string Name, string FileName, Action<string> Write);

    private static readonly InputCase[] Cases =
    {
        // 横長: 縦 768 基準で縮小（2304×768）。DPI 72 で、DPI を無視することも確かめる
        new("landscape", "landscape_3000x1000.png", p => SavePng(CreatePattern(3000, 1000, dpi: 72), p)),
        // 縦長: 横 1024 基準で縮小（1024×1365）
        new("portrait", "portrait_1500x2000.png", p => SavePng(CreatePattern(1500, 2000), p)),
        // 4:3: ちょうど 1/2 に縮小。DPI 300
        new("ratio4x3", "ratio4x3_2048x1536.png", p => SavePng(CreatePattern(2048, 1536, dpi: 300), p)),
        // 小さい画像: 1.28 倍に拡大
        new("small", "small_800x600.png", p => SavePng(CreatePattern(800, 600), p)),
        // とても横長で小さい画像: 7.68 倍に拡大（3840×768）
        new("wide_small", "wide_small_500x100.png", p => SavePng(CreatePattern(500, 100), p)),
        // 透過 PNG: 左 1/3 が透明・中 1/3 が半透明・右 1/3 が不透明。白で塗られる
        new("transparent", "transparent_1600x1200.png", p => SavePng(CreatePattern(1600, 1200, withAlpha: true), p)),
        // 回転情報あり: TIFF の Orientation = 6（時計回り 90°）。読み込むと 1200×1600 になる
        new("orientation6", "orientation6_1600x1200.tif", p => SaveTiffWithOrientation(CreatePattern(1600, 1200), 6, p)),
        // パレット画像（GIF）: 16:9 で縦 768 基準（1365×768）
        new("palette", "palette_1600x900.gif", p => SaveGif(CreatePalettePattern(1600, 900), p)),
        // 16 bit グレースケール PNG: 8 bit への変換を確かめる（1024×1536）
        new("gray16", "gray16_1000x1500.png", p => SavePng(CreateGray16Pattern(1000, 1500), p)),
    };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("使い方: BaselineGenerator <出力フォルダ> [--force]");
            return 1;
        }

        var root = Path.GetFullPath(args[0]);
        var inputs = Path.Combine(root, "inputs");
        var expected = Path.Combine(root, "expected");

        if (Directory.Exists(inputs) || Directory.Exists(expected))
        {
            if (!args.Contains("--force"))
            {
                Console.Error.WriteLine("基準ファイルが既にあります。置き換えた後に作り直すと基準の意味が無くなるため、"
                    + "本当に作り直すときだけ --force を付けてください。");
                return 2;
            }
            if (Directory.Exists(inputs)) Directory.Delete(inputs, recursive: true);
            if (Directory.Exists(expected)) Directory.Delete(expected, recursive: true);
        }
        Directory.CreateDirectory(inputs);
        Directory.CreateDirectory(expected);

        foreach (var c in Cases)
        {
            var inputPath = Path.Combine(inputs, c.FileName);
            c.Write(inputPath);
            Process(c.Name, inputPath, expected);
        }
        return 0;
    }

    /// <summary>CoverCropper の MainViewModel と同じ順番で処理し、途中結果と出力を保存する</summary>
    private static void Process(string name, string inputPath, string expectedFolder)
    {
        var resizer = new ImageResizer();
        var cropper = new ImageCropper();

        var loaded = new ImageLoader().Load(inputPath);
        var (w, h) = resizer.CalculateSize(loaded.PixelWidth, loaded.PixelHeight);
        var resized = resizer.Resize(loaded, w, h);
        var (x, y) = cropper.Center(w, h);
        var cropped = cropper.Crop(resized, x, y);

        SavePng(loaded, Path.Combine(expectedFolder, $"{name}.loaded.png"));
        SavePng(resized, Path.Combine(expectedFolder, $"{name}.resized.png"));
        new JpegSaver().Save(cropped, Path.Combine(expectedFolder, $"{name}.output.jpg"), AppConstants.JpegQuality);

        Console.WriteLine($"{name}: 読み込み {loaded.PixelWidth}x{loaded.PixelHeight} → リサイズ {w}x{h} → 切り抜き位置 ({x}, {y})");
    }

    // ---------------------------------------------------------------- 入力画像の模様

    /// <summary>
    /// なめらかな変化（横・縦のグラデーション）と細かい変化（8px の市松・1px の斜め線・円の縁）を持つ模様。
    /// リサイズの補間の違いが画素の値に表れるようにする。市松は横帯（4 本に 1 本）だけにして、基準ファイルを小さく保つ。
    /// </summary>
    private static (byte B, byte G, byte R) PatternAt(int x, int y, int width, int height)
    {
        int r = x * 255 / (width - 1);
        int g = y * 255 / (height - 1);
        int b = (y / 96) % 4 != 0 ? 128 : ((x / 8 + y / 8) & 1) == 0 ? 40 : 215;

        if ((x + y) % 37 == 0)
        {
            r = 255 - r;
            g = 255 - g;
            b = 255 - b;
        }

        int inner = Math.Min(width, height) * 3 / 10, outer = inner + 6;
        long dx = x - width / 2, dy = y - height / 2, d2 = dx * dx + dy * dy;
        if (d2 >= (long)inner * inner && d2 < (long)outer * outer)
            r = g = b = 0;

        return ((byte)b, (byte)g, (byte)r);
    }

    private static BitmapSource CreatePattern(int width, int height, bool withAlpha = false, double dpi = 96)
    {
        int bpp = withAlpha ? 4 : 3;
        int stride = width * bpp;
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var (b, g, r) = PatternAt(x, y, width, height);
            int i = y * stride + x * bpp;
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            if (withAlpha) pixels[i + 3] = AlphaAt(x, width);
        }
        var format = withAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24;
        var bmp = BitmapSource.Create(width, height, dpi, dpi, format, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>左 1/3 は透明、中 1/3 は左から右へ 0 → 255、右 1/3 は不透明</summary>
    private static byte AlphaAt(int x, int width)
    {
        int third = width / 3;
        if (x < third) return 0;
        if (x >= third * 2) return 255;
        return (byte)((x - third) * 255 / (third - 1));
    }

    /// <summary>模様を 6×6×6 のカラーキューブ（216 色）に割り当てたパレット画像</summary>
    private static BitmapSource CreatePalettePattern(int width, int height)
    {
        var colors = new List<Color>(256);
        for (int r = 0; r < 6; r++)
        for (int g = 0; g < 6; g++)
        for (int b = 0; b < 6; b++)
            colors.Add(Color.FromRgb((byte)(r * 51), (byte)(g * 51), (byte)(b * 51)));
        while (colors.Count < 256) colors.Add(Colors.Black);

        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var (b, g, r) = PatternAt(x, y, width, height);
            pixels[y * width + x] = (byte)((r * 5 + 127) / 255 * 36 + (g * 5 + 127) / 255 * 6 + (b * 5 + 127) / 255);
        }
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Indexed8, new BitmapPalette(colors), pixels, width);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>模様の明るさを 16 bit にした画像。下位 8 bit も変化させ、8 bit への丸めの違いが表れるようにする</summary>
    private static BitmapSource CreateGray16Pattern(int width, int height)
    {
        int stride = width * 2;
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var (b, g, r) = PatternAt(x, y, width, height);
            int gray = (r * 299 + g * 587 + b * 114) / 1000;
            int value = gray * 256 + ((x * 31 + y * 17) & 0xFF);
            int i = y * stride + x * 2;
            pixels[i] = (byte)(value & 0xFF);
            pixels[i + 1] = (byte)(value >> 8);
        }
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray16, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    // ---------------------------------------------------------------- 保存

    private static void SavePng(BitmapSource image, string path)
        => Save(new PngBitmapEncoder(), BitmapFrame.Create(image), path);

    private static void SaveGif(BitmapSource image, string path)
        => Save(new GifBitmapEncoder(), BitmapFrame.Create(image), path);

    private static void SaveTiffWithOrientation(BitmapSource image, ushort orientation, string path)
    {
        var metadata = new BitmapMetadata("tiff");
        metadata.SetQuery("/ifd/{ushort=274}", orientation);
        Save(new TiffBitmapEncoder { Compression = TiffCompressOption.Zip }, BitmapFrame.Create(image, null, metadata, null), path);
    }

    private static void Save(BitmapEncoder encoder, BitmapFrame frame, string path)
    {
        encoder.Frames.Add(frame);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }
}
