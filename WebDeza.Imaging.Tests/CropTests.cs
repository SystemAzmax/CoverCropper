using WebDeza.Imaging.Operations;

namespace WebDeza.Imaging.Tests;

public class CropTests
{
    [Theory] // E04: 任意の矩形（7×5 の画像から）
    [InlineData(1, 1, 3, 2)]  // 中ほど
    [InlineData(0, 0, 4, 3)]  // 左上の角から
    [InlineData(4, 2, 3, 3)]  // 右下の角まで
    [InlineData(0, 2, 7, 1)]  // 幅いっぱいの 1 行
    [InlineData(3, 0, 1, 5)]  // 高さいっぱいの 1 列
    [InlineData(6, 4, 1, 1)]  // 右下の 1 画素
    [InlineData(0, 0, 1, 1)]  // 左上の 1 画素
    [InlineData(0, 0, 6, 5)]  // 幅だけ 1 小さい
    [InlineData(0, 0, 7, 4)]  // 高さだけ 1 小さい
    public void Apply_ReturnsPixelsOfRect(int x, int y, int width, int height)
    {
        var source = TestBuffers.Distinct(7, 5);

        var result = Crop.Apply(source, new PixelRect(x, y, width, height));

        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
        Assert.Equal(width * height * 4, result.Pixels.Length);
        for (int dy = 0; dy < height; dy++)
        for (int dx = 0; dx < width; dx++)
            Assert.Equal(TestBuffers.PixelAt(source, x + dx, y + dy), TestBuffers.PixelAt(result, dx, dy));
    }

    [Theory] // E04: はみ出す矩形（7×5 の画像に対して）
    [InlineData(-1, 0, 3, 2)]            // 左にはみ出す
    [InlineData(0, -1, 3, 2)]            // 上にはみ出す
    [InlineData(5, 0, 3, 2)]             // 右にはみ出す
    [InlineData(0, 4, 3, 2)]             // 下にはみ出す
    [InlineData(0, 0, 8, 5)]             // 幅が画像より大きい
    [InlineData(0, 0, 7, 6)]             // 高さが画像より大きい
    [InlineData(7, 0, 1, 1)]             // 画像の右の外
    [InlineData(0, 5, 1, 1)]             // 画像の下の外
    [InlineData(1, 1, 0, 2)]             // 幅が 0
    [InlineData(1, 1, 3, 0)]             // 高さが 0
    [InlineData(1, 1, -3, 2)]            // 幅が負
    [InlineData(1, 1, 3, -2)]            // 高さが負
    [InlineData(int.MaxValue, 0, 2, 1)]  // X + 幅が int の範囲を超える
    [InlineData(0, int.MaxValue, 1, 2)]  // Y + 高さが int の範囲を超える
    [InlineData(1, 1, int.MaxValue, 1)]
    public void Apply_RectOutsideImage_Throws(int x, int y, int width, int height)
    {
        var source = TestBuffers.Distinct(7, 5);

        Assert.Throws<ArgumentOutOfRangeException>(() => Crop.Apply(source, new PixelRect(x, y, width, height)));
    }

    [Fact]
    public void Apply_WholeImage_ReturnsInput()
    {
        var source = TestBuffers.Distinct(7, 5);

        Assert.Same(source, Crop.Apply(source, new PixelRect(0, 0, 7, 5)));
    }

    [Fact]
    public void Apply_DoesNotModifyInput()
    {
        var source = TestBuffers.Distinct(7, 5);
        var before = (byte[])source.Pixels.Clone();

        var result = Crop.Apply(source, new PixelRect(2, 1, 4, 3));

        Assert.NotSame(source.Pixels, result.Pixels);
        Assert.Equal(before, source.Pixels);
    }

    [Fact]
    public void Apply_LargerImage_MatchesSourceAtOffset()
    {
        // 表紙画像と同じ形の切り出し（幅の広い画像の中央から）
        var source = TestBuffers.Distinct(240, 80);

        var result = Crop.Apply(source, new PixelRect(67, 0, 107, 80));

        Assert.Equal(TestBuffers.PixelAt(source, 67, 0), TestBuffers.PixelAt(result, 0, 0));
        Assert.Equal(TestBuffers.PixelAt(source, 173, 79), TestBuffers.PixelAt(result, 106, 79));
        Assert.Equal(TestBuffers.PixelAt(source, 120, 40), TestBuffers.PixelAt(result, 53, 40));
    }

    [Fact]
    public void Apply_OpaqueImage_ResultIsOpaque()
    {
        var source = CreateHalfTransparent(4, 2, transparentColumns: 0);

        Assert.True(Crop.Apply(source, new PixelRect(1, 0, 2, 2)).IsOpaque);
    }

    [Fact]
    public void Apply_ImageWithAlpha_IsOpaqueDependsOnRect()
    {
        // 左の 2 列が半透明、右の 2 列が不透明な 4×2 の画像
        var source = CreateHalfTransparent(4, 2, transparentColumns: 2);
        Assert.False(source.IsOpaque);

        Assert.True(Crop.Apply(source, new PixelRect(2, 0, 2, 2)).IsOpaque);   // 不透明な部分だけ
        Assert.False(Crop.Apply(source, new PixelRect(1, 0, 2, 2)).IsOpaque);  // 半透明の列を含む
        Assert.False(Crop.Apply(source, new PixelRect(0, 0, 2, 1)).IsOpaque);  // 半透明の部分だけ
    }

    [Fact]
    public void Apply_CancelledToken_Throws()
    {
        var source = TestBuffers.Distinct(7, 5);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => Crop.Apply(source, new PixelRect(1, 1, 3, 2), cts.Token));
    }

    private static ImageBuffer CreateHalfTransparent(int width, int height, int transparentColumns)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            pixels[i] = (byte)(x * 10);
            pixels[i + 1] = (byte)(y * 10);
            pixels[i + 2] = 200;
            pixels[i + 3] = x < transparentColumns ? (byte)128 : (byte)255;
        }
        return ImageBuffer.Create(width, height, pixels);
    }
}
