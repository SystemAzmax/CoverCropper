namespace WebDeza.Imaging.Tests;

public class ImageBufferTests
{
    [Fact]
    public void Create_KeepsSizeAndPixelArray()
    {
        var pixels = new byte[3 * 2 * 4];

        var image = ImageBuffer.Create(3, 2, pixels);

        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        // 大きな画像を複製しないよう、渡した配列をそのまま持つ
        Assert.Same(pixels, image.Pixels);
    }

    [Theory]
    [InlineData(3 * 2 * 4 - 1)]
    [InlineData(3 * 2 * 4 + 1)]
    [InlineData(3 * 2 * 3)] // Bgr24 の長さ
    [InlineData(0)]
    public void Create_PixelLengthMismatch_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => ImageBuffer.Create(3, 2, new byte[length]));
    }

    [Fact]
    public void Create_NullPixels_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ImageBuffer.Create(3, 2, null!));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(3, 0)]
    [InlineData(-1, 2)]
    [InlineData(3, -1)]
    public void Create_NonPositiveSize_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageBuffer.Create(width, height, new byte[24]));
    }

    [Fact]
    public void Create_AllAlpha255_IsOpaque()
    {
        var image = ImageBuffer.Create(3, 2, CreatePixels(3, 2, alpha: 255));

        Assert.True(image.IsOpaque);
    }

    [Theory]
    [InlineData(0, 254)]  // 最初の画素
    [InlineData(5, 0)]    // 最後の画素
    [InlineData(2, 128)]
    public void Create_AnyAlphaBelow255_IsNotOpaque(int pixelIndex, byte alpha)
    {
        var pixels = CreatePixels(3, 2, alpha: 255);
        pixels[pixelIndex * 4 + 3] = alpha;

        var image = ImageBuffer.Create(3, 2, pixels);

        Assert.False(image.IsOpaque);
    }

    [Fact]
    public void Create_ColorChannelsDoNotAffectIsOpaque()
    {
        // B・G・R が 0 でも、アルファが 255 なら不透明
        var pixels = new byte[3 * 2 * 4];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        Assert.True(ImageBuffer.Create(3, 2, pixels).IsOpaque);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_ExplicitIsOpaque_IsUsedAsGiven(bool isOpaque)
    {
        // 呼び出し側が分かっているときは調べ直さない（透過のある画素でも、渡した値のままになる）
        var pixels = CreatePixels(3, 2, alpha: isOpaque ? (byte)0 : (byte)255);

        var image = ImageBuffer.Create(3, 2, pixels, isOpaque);

        Assert.Equal(isOpaque, image.IsOpaque);
    }

    [Theory]
    [InlineData(23171, 23171)]              // 2,147,580,964 バイト: 上限をわずかに超える
    [InlineData(32768, 32768)]              // 4 GB
    [InlineData(65536, 65536)]              // int の範囲を超える画素数
    [InlineData(int.MaxValue, int.MaxValue)] // 幅 × 高さ × 4 が long でもあふれる
    [InlineData(int.MaxValue, 1)]
    public void Create_TooLarge_ThrowsImageTooLargeException(int width, int height)
    {
        // 上限の検証は配列の長さの検証より先に行うので、大きな配列を用意しなくても確かめられる
        var ex = Assert.Throws<ImageTooLargeException>(() => ImageBuffer.Create(width, height, new byte[4]));

        Assert.Equal(width, ex.Width);
        Assert.Equal(height, ex.Height);
        Assert.Throws<ImageTooLargeException>(() => ImageBuffer.GetByteLength(width, height));
    }

    [Theory]
    [InlineData(1, 1, 4)]
    [InlineData(1024, 768, 3_145_728)]
    [InlineData(8000, 6000, 192_000_000)]
    [InlineData(23170, 23170, 2_147_395_600)] // 上限（Array.MaxLength = 2,147,483,591）のすぐ下
    public void GetByteLength_ReturnsWidthTimesHeightTimes4(int width, int height, int expected)
    {
        Assert.Equal(expected, ImageBuffer.GetByteLength(width, height));
    }

    [Fact]
    public void GetByteLength_LargestAllowed_FitsInArray()
    {
        // 536,870,897 画素 × 4 = 2,147,483,588 バイト。1 画素増やすと上限を超える
        const int maxPixels = 536_870_897;

        Assert.True(ImageBuffer.GetByteLength(maxPixels, 1) <= Array.MaxLength);
        Assert.Throws<ImageTooLargeException>(() => ImageBuffer.GetByteLength(maxPixels + 1, 1));
    }

    private static byte[] CreatePixels(int width, int height, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 10;
            pixels[i + 1] = 20;
            pixels[i + 2] = 30;
            pixels[i + 3] = alpha;
        }
        return pixels;
    }
}
