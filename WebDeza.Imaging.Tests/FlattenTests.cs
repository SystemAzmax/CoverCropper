using WebDeza.Imaging.Operations;

namespace WebDeza.Imaging.Tests;

public class FlattenTests
{
    private static readonly RgbColor White = new(255, 255, 255);
    private static readonly RgbColor Black = new(0, 0, 0);

    [Fact] // E08
    public void Apply_HalfTransparentRedOnWhite_MatchesCoverCropperBlend()
    {
        // 半透明の赤 (B=0, G=0, R=255, A=128)
        var source = Solid(4, 3, b: 0, g: 0, r: 255, a: 128);

        var result = Flatten.Apply(source, White);

        // CoverCropper の式: (c × a + 255 × (255 − a) + 127) ÷ 255
        //   B・G: (0 × 128 + 255 × 127 + 127) ÷ 255 = 32512 ÷ 255 = 127
        //   R:    (255 × 128 + 255 × 127 + 127) ÷ 255 = 65152 ÷ 255 = 255
        Assert.True(result.IsOpaque);
        for (int i = 0; i < result.Pixels.Length; i += 4)
        {
            Assert.Equal(127, result.Pixels[i]);
            Assert.Equal(127, result.Pixels[i + 1]);
            Assert.Equal(255, result.Pixels[i + 2]);
            Assert.Equal(255, result.Pixels[i + 3]);
        }
    }

    [Fact] // E08: 色とアルファのすべての組み合わせ
    public void Apply_OnWhite_MatchesCoverCropperBlendForEveryColorAndAlpha()
    {
        // 横がアルファ 0〜255、縦が色 0〜255 の画像
        var source = ColorByAlphaGrid();

        var result = Flatten.Apply(source, White);

        for (int c = 0; c < 256; c++)
        for (int a = 0; a < 256; a++)
        {
            int i = (c * 256 + a) * 4;
            Assert.Equal(CoverCropperBlendOnWhite(source.Pixels[i], a), result.Pixels[i]);
            Assert.Equal(CoverCropperBlendOnWhite(source.Pixels[i + 1], a), result.Pixels[i + 1]);
            Assert.Equal(CoverCropperBlendOnWhite(source.Pixels[i + 2], a), result.Pixels[i + 2]);
            Assert.Equal(255, result.Pixels[i + 3]);
        }
    }

    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    [InlineData(128, 128, 128)]
    [InlineData(10, 128, 250)]
    public void Apply_FullyTransparentPixels_BecomeBackground(byte r, byte g, byte b)
    {
        // 透明な画素が持っている色（ここでは緑）は、結果に残らない
        var source = Solid(4, 3, b: 0, g: 255, r: 0, a: 0);

        var result = Flatten.Apply(source, new RgbColor(r, g, b));

        for (int i = 0; i < result.Pixels.Length; i += 4)
        {
            Assert.Equal(b, result.Pixels[i]);
            Assert.Equal(g, result.Pixels[i + 1]);
            Assert.Equal(r, result.Pixels[i + 2]);
            Assert.Equal(255, result.Pixels[i + 3]);
        }
    }

    [Fact]
    public void Apply_OpaquePixelsInImageWithAlpha_KeepColor()
    {
        // 1 画素だけ透明で、ほかは不透明な画像
        var source = TestBuffers.Distinct(5, 4);
        var pixels = (byte[])source.Pixels.Clone();
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        pixels[3] = 0;
        var image = ImageBuffer.Create(5, 4, pixels);
        Assert.False(image.IsOpaque);

        var result = Flatten.Apply(image, new RgbColor(10, 128, 250));

        // 透明だった左上は背景色（R=10, G=128, B=250）
        Assert.Equal(new byte[] { 250, 128, 10, 255 }, result.Pixels[..4]);
        // 不透明だった画素はそのまま
        Assert.Equal(pixels[4..], result.Pixels[4..]);
    }

    [Fact]
    public void Apply_HalfTransparentOnColoredBackground_BlendsEachChannel()
    {
        // (B=200, G=100, R=0, A=51) を、背景 (R=255, G=50, B=0) に重ねる。51 ÷ 255 = 0.2
        var source = Solid(2, 2, b: 200, g: 100, r: 0, a: 51);

        var result = Flatten.Apply(source, new RgbColor(255, 50, 0));

        // B: 200 × 0.2 + 0 × 0.8 = 40   G: 100 × 0.2 + 50 × 0.8 = 60   R: 0 × 0.2 + 255 × 0.8 = 204
        Assert.Equal(new byte[] { 40, 60, 204, 255 }, result.Pixels[..4]);
    }

    [Fact]
    public void Apply_HalfTransparentBlackOnBlack_StaysBlack()
    {
        var result = Flatten.Apply(Solid(2, 2, b: 0, g: 0, r: 0, a: 128), Black);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, result.Pixels[..4]);
    }

    [Fact]
    public void Apply_OpaqueImage_ReturnsInput()
    {
        var source = Solid(4, 3, b: 10, g: 20, r: 30, a: 255);

        Assert.Same(source, Flatten.Apply(source, White));
    }

    [Fact]
    public void Apply_DoesNotModifyInput()
    {
        var source = ColorByAlphaGrid();
        var before = (byte[])source.Pixels.Clone();

        var result = Flatten.Apply(source, White);

        Assert.NotSame(source.Pixels, result.Pixels);
        Assert.Equal(before, source.Pixels);
        Assert.Equal((source.Width, source.Height), (result.Width, result.Height));
    }

    [Fact]
    public void Apply_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => Flatten.Apply(Solid(4, 3, b: 0, g: 0, r: 255, a: 128), White, cts.Token));
    }

    /// <summary>CoverCropper の ImageLoader.ToBgr24OnWhite と同じ式</summary>
    private static int CoverCropperBlendOnWhite(int color, int alpha)
    {
        if (alpha == 255) return color;
        int inv = 255 * (255 - alpha);
        return (color * alpha + inv + 127) / 255;
    }

    /// <summary>256×256。横 (x) がアルファ、縦 (y) が色。3 つのチャンネルには違う値を入れる</summary>
    private static ImageBuffer ColorByAlphaGrid()
    {
        var pixels = new byte[256 * 256 * 4];
        for (int c = 0; c < 256; c++)
        for (int a = 0; a < 256; a++)
        {
            int i = (c * 256 + a) * 4;
            pixels[i] = (byte)c;
            pixels[i + 1] = (byte)(255 - c);
            pixels[i + 2] = (byte)(c * 7 % 256);
            pixels[i + 3] = (byte)a;
        }
        return ImageBuffer.Create(256, 256, pixels);
    }

    private static ImageBuffer Solid(int width, int height, byte b, byte g, byte r, byte a)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }
        return ImageBuffer.Create(width, height, pixels);
    }
}
