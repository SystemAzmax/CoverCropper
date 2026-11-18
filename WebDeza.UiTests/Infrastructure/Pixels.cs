using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>テスト画像の黄色い矢印の向き</summary>
public enum ArrowDirection { None, Up, Right, Down, Left }

/// <summary>画素（Bgra32）。画面のキャプチャと、保存したファイルを比べるのに使う</summary>
public sealed class Pixels(int width, int height, byte[] bgra)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Bgra { get; } = bgra;

    /// <summary>画面の矩形（物理ピクセル）を取り込む。1 回に約 0.5 秒かかるので、比べる前の絵は先に取っておく</summary>
    public static Pixels CaptureScreen(Rectangle r)
    {
        Dpi.EnsureThread();
        using var bitmap = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size);
        var data = bitmap.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var px = new byte[r.Width * r.Height * 4];
            for (int y = 0; y < r.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * r.Width * 4, r.Width * 4);
            return new Pixels(r.Width, r.Height, px);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>画像ファイルの画素をそのまま読む（EXIF の向きは使わない）</summary>
    public static Pixels Load(string path) => StaThread.Run(() =>
    {
        using var fs = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var px = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(px, converted.PixelWidth * 4, 0);
        return new Pixels(converted.PixelWidth, converted.PixelHeight, px);
    });

    /// <summary>PNG で書く（失敗したときに見るため）</summary>
    public void SavePng(string path) => StaThread.Run(() =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var source = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Bgra, Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var fs = File.Create(path);
        encoder.Save(fs);
    });

    /// <summary>縮小した色の格子（大きさの違う絵を比べるため）</summary>
    public double[] Thumb(int gridWidth = 48, int gridHeight = 36)
    {
        var sum = new double[gridWidth * gridHeight * 3];
        var count = new int[gridWidth * gridHeight];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int k = y * gridHeight / Height * gridWidth + x * gridWidth / Width;
                int i = (y * Width + x) * 4;
                sum[k * 3] += Bgra[i];
                sum[k * 3 + 1] += Bgra[i + 1];
                sum[k * 3 + 2] += Bgra[i + 2];
                count[k]++;
            }
        for (int k = 0; k < count.Length; k++)
            for (int j = 0; j < 3; j++) sum[k * 3 + j] /= Math.Max(1, count[k]);
        return sum;
    }

    /// <summary>縮小した絵どうしの、色の差の平均（0〜255）</summary>
    public static double ThumbDiff(Pixels a, Pixels b) => a.Thumb().Zip(b.Thumb(), (x, y) => Math.Abs(x - y)).Average();

    /// <summary>画素ごとの色の差の平均（0〜255。アルファは比べない）。大きさが違えば、縮小して比べる（ThumbDiff）</summary>
    public static double PixelDiff(Pixels a, Pixels b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return ThumbDiff(a, b);
        long sum = 0;
        for (int i = 0; i < a.Bgra.Length; i += 4)
            sum += Math.Abs(a.Bgra[i] - b.Bgra[i]) + Math.Abs(a.Bgra[i + 1] - b.Bgra[i + 1]) + Math.Abs(a.Bgra[i + 2] - b.Bgra[i + 2]);
        return a.Width * a.Height == 0 ? 0 : sum / (3.0 * a.Width * a.Height);
    }

    /// <summary>左右を反転した絵</summary>
    public Pixels MirrorHorizontal()
    {
        var o = new byte[Bgra.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                Buffer.BlockCopy(Bgra, (y * Width + x) * 4, o, (y * Width + (Width - 1 - x)) * 4, 4);
        return new Pixels(Width, Height, o);
    }

    /// <summary>上下を反転した絵</summary>
    public Pixels MirrorVertical()
    {
        var o = new byte[Bgra.Length];
        for (int y = 0; y < Height; y++) Buffer.BlockCopy(Bgra, y * Width * 4, o, (Height - 1 - y) * Width * 4, Width * 4);
        return new Pixels(Width, Height, o);
    }

    /// <summary>右に 90° 回した絵（幅と高さが入れ替わる）</summary>
    public Pixels RotateRight()
    {
        int w = Height, h = Width;
        var o = new byte[Bgra.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                Buffer.BlockCopy(Bgra, (y * Width + x) * 4, o, (x * w + (w - 1 - y)) * 4, 4);
        return new Pixels(w, h, o);
    }

    /// <summary>同じ大きさの 2 枚の、範囲 [x0, x1)×[y0, y1) の中の色の差の平均（0〜255。範囲は絵の中に切り詰める）</summary>
    public static double RegionDiff(Pixels a, Pixels b, int x0, int y0, int x1, int y1)
    {
        if (a.Width != b.Width || a.Height != b.Height) throw new ArgumentException("大きさが違う");
        long sum = 0, n = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(a.Height, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(a.Width, x1); x++)
            {
                int i = (y * a.Width + x) * 4;
                sum += Math.Abs(a.Bgra[i] - b.Bgra[i]) + Math.Abs(a.Bgra[i + 1] - b.Bgra[i + 1]) + Math.Abs(a.Bgra[i + 2] - b.Bgra[i + 2]);
                n += 3;
            }
        return n == 0 ? 0 : (double)sum / n;
    }

    /// <summary>彩度の平均（画素ごとの RGB の最大と最小の差。白黒なら 0）</summary>
    public double Chroma()
    {
        long sum = 0;
        for (int i = 0; i < Bgra.Length; i += 4)
            sum += Math.Max(Bgra[i], Math.Max(Bgra[i + 1], Bgra[i + 2])) - Math.Min(Bgra[i], Math.Min(Bgra[i + 1], Bgra[i + 2]));
        return Bgra.Length == 0 ? 0 : (double)sum / (Bgra.Length / 4);
    }

    /// <summary>その色（BGR がそれぞれ tolerance 未満の差）の画素の数</summary>
    public int CountColor(byte r, byte g, byte b, int tolerance = 10)
    {
        int n = 0;
        for (int i = 0; i < Bgra.Length; i += 4)
            if (Math.Abs(Bgra[i] - b) < tolerance && Math.Abs(Bgra[i + 1] - g) < tolerance && Math.Abs(Bgra[i + 2] - r) < tolerance) n++;
        return n;
    }

    /// <summary>黄色い三角（テスト画像の矢印）の向き。とがった側は画素が少ない</summary>
    public ArrowDirection Arrow(out string detail)
    {
        var points = new List<(int X, int Y)>();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = (y * Width + x) * 4;
                if (Bgra[i + 2] > 210 && Bgra[i + 1] > 210 && Bgra[i] < 70) points.Add((x, y));
            }
        if (points.Count < 20)
        {
            detail = "黄色が無い";
            return ArrowDirection.None;
        }

        // 離れた所の黄色っぽい画素を除くため、範囲は 2%〜98% の位置で決める
        var xs = points.Select(p => p.X).Order().ToList();
        var ys = points.Select(p => p.Y).Order().ToList();
        int minX = xs[xs.Count * 2 / 100], maxX = xs[xs.Count * 98 / 100];
        int minY = ys[ys.Count * 2 / 100], maxY = ys[ys.Count * 98 / 100];
        points = points.Where(p => p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY).ToList();
        double bw = maxX - minX + 1, bh = maxY - minY + 1;
        int top = points.Count(p => p.Y < minY + bh * 0.3), bottom = points.Count(p => p.Y > maxY - bh * 0.3);
        int left = points.Count(p => p.X < minX + bw * 0.3), right = points.Count(p => p.X > maxX - bw * 0.3);
        double cx = points.Average(p => p.X) / Width, cy = points.Average(p => p.Y) / Height;
        detail = $"重心 ({cx:F2}, {cy:F2}) 上 {top} 下 {bottom} 左 {left} 右 {right}";

        bool balancedX = Math.Abs(left - right) < (left + right) * 0.35;
        bool balancedY = Math.Abs(top - bottom) < (top + bottom) * 0.35;
        if (top * 1.5 < bottom && balancedX) return ArrowDirection.Up;
        if (bottom * 1.5 < top && balancedX) return ArrowDirection.Down;
        if (right * 1.5 < left && balancedY) return ArrowDirection.Right;
        if (left * 1.5 < right && balancedY) return ArrowDirection.Left;
        return ArrowDirection.None;
    }
}
