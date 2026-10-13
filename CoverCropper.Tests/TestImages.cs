using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverCropper.Tests;

internal static class TestImages
{
    /// <summary>各ピクセル値を (x, y) から決まる値で埋めた Bgr24 画像</summary>
    public static BitmapSource CreateGradient(int width, int height, double dpi = 96)
    {
        int stride = width * 3;
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * stride + x * 3;
            pixels[i] = (byte)(x % 256);
            pixels[i + 1] = (byte)(y % 256);
            pixels[i + 2] = (byte)((x + y) % 256);
        }
        var bmp = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgr24, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    public static BitmapSource CreateSolid(int width, int height, Color color, PixelFormat format)
    {
        int bpp = (format.BitsPerPixel + 7) / 8;
        int stride = width * bpp;
        var pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += bpp)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            if (bpp == 4) pixels[i + 3] = color.A;
        }
        var bmp = BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    public static void Save(BitmapEncoder encoder, BitmapSource image, string path)
    {
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static byte[] GetPixel(BitmapSource image, int x, int y)
    {
        var converted = image.Format == PixelFormats.Bgr24
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        var buffer = new byte[3];
        converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), buffer, 3, 0);
        return buffer;
    }
}

/// <summary>テストごとの一時フォルダ</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CoverCropperTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
