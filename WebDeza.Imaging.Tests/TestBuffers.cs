namespace WebDeza.Imaging.Tests;

/// <summary>テスト用の画像を作る補助</summary>
internal static class TestBuffers
{
    /// <summary>
    /// 画素ごとに値が違う画像（幅 × 高さが 63,001 画素まで、すべての画素が互いに違う）。
    /// アルファも画素ごとに変えるので、4 つのチャンネルがまとまって動いているかを確かめられる。
    /// </summary>
    public static ImageBuffer Distinct(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            int i = index * 4;
            pixels[i] = (byte)(index % 251);
            pixels[i + 1] = (byte)(index / 251 % 251);
            pixels[i + 2] = (byte)((x * 7 + y * 13) % 256);
            pixels[i + 3] = (byte)(255 - index % 3);
        }
        return ImageBuffer.Create(width, height, pixels);
    }

    /// <summary>Distinct と同じ色で、アルファをすべて 255 にした（不透明な）画像</summary>
    public static ImageBuffer DistinctOpaque(int width, int height)
    {
        var pixels = Distinct(width, height).Pixels;
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return ImageBuffer.Create(width, height, pixels, isOpaque: true);
    }

    /// <summary>(x, y) の画素を B・G・R・A をまとめた 1 つの値で返す</summary>
    public static uint PixelAt(ImageBuffer image, int x, int y)
        => BitConverter.ToUInt32(image.Pixels, (y * image.Width + x) * 4);

    public static void AssertSameImage(ImageBuffer expected, ImageBuffer actual)
    {
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        Assert.True(expected.Pixels.AsSpan().SequenceEqual(actual.Pixels), "画素が一致しません");
    }
}
