using System.IO;
using System.Windows.Media.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Imaging.Wic;

namespace WebDeza.Imaging.Tests;

/// <summary>WIC プラグインのテストの補助</summary>
internal static class WicTestHelper
{
    /// <summary>標準の形式を登録したレジストリ</summary>
    public static FormatRegistry CreateRegistry()
    {
        var registry = new FormatRegistry();
        registry.AddWicFormats();
        return registry;
    }

    /// <summary>テスト用のファイルを、WPF のエンコーダーで直接作る（プラグインのエンコーダーを通さない）</summary>
    public static string SaveWithWpf(TempFolder temp, string name, BitmapEncoder encoder, BitmapSource source,
        BitmapMetadata? metadata = null)
    {
        encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        var path = temp.Combine(name);
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    /// <summary>プラグインのエンコーダーで、メモリ上にエンコードする</summary>
    public static byte[] Encode(FormatRegistry registry, string formatId, ImageBuffer image, ParameterValues? options = null)
    {
        using var stream = new MemoryStream();
        registry.FindById(formatId)!.Encoder!.Encode(image, options ?? ParameterValues.Empty, stream, CancellationToken.None);
        return stream.ToArray();
    }

    /// <summary>プラグインのデコーダーで、メモリ上のファイルの中身を読む</summary>
    public static DecodedImage Decode(FormatRegistry registry, string formatId, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return registry.FindById(formatId)!.Decoder!.Decode(stream, CancellationToken.None);
    }

    /// <summary>
    /// 透明・半透明・不透明の部分を持つ画像。左 1/3 は透明（色は持っている）、中 1/3 は半透明、右 1/3 は不透明
    /// </summary>
    public static ImageBuffer WithAlpha(int width, int height)
    {
        var pixels = TestBuffers.Distinct(width, height).Pixels;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            pixels[(y * width + x) * 4 + 3] =
                x < width / 3 ? (byte)0 : x < width * 2 / 3 ? (byte)(40 + (x * 7 + y * 3) % 200) : (byte)255;
        }
        return ImageBuffer.Create(width, height, pixels);
    }

    /// <summary>細かい変化のある不透明な写真風の画像（JPEG の品質でサイズがはっきり変わるように）</summary>
    public static ImageBuffer Photo(int width, int height)
    {
        var random = new Random(12345);
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            int noise = random.Next(-24, 25);
            pixels[i] = (byte)Math.Clamp(x * 255 / width + noise, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(y * 255 / height + noise, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp((x + y) % 256 + noise, 0, 255);
            pixels[i + 3] = 255;
        }
        return ImageBuffer.Create(width, height, pixels);
    }
}
