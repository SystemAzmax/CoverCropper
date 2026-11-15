using WebDeza.Imaging.Operations;

namespace WebDeza.Imaging.Tests;

public class PasteTests
{
    [Theory] // E32: 中ほど・角・端に貼る（7×5 の画像に）
    [InlineData(2, 1, 3, 2)]  // 中ほど
    [InlineData(0, 0, 2, 2)]  // 左上の角
    [InlineData(4, 2, 3, 3)]  // 右下の角まで
    [InlineData(0, 4, 7, 1)]  // 下の 1 行全体
    [InlineData(6, 0, 1, 5)]  // 右の 1 列全体
    public void Apply_ReplacesOnlyTheRange(int x, int y, int width, int height)
    {
        var image = TestBuffers.Distinct(7, 5);
        var original = (byte[])image.Pixels.Clone();
        var patch = Solid(width, height, 0x11223344);

        var result = Paste.Apply(image, patch, x, y);

        Assert.Equal((7, 5), (result.Width, result.Height));
        for (int py = 0; py < 5; py++)
        for (int px = 0; px < 7; px++)
        {
            bool inside = px >= x && px < x + width && py >= y && py < y + height;
            uint expected = inside ? 0x11223344u : TestBuffers.PixelAt(image, px, py);
            Assert.Equal(expected, TestBuffers.PixelAt(result, px, py));
        }
        Assert.Equal(original, image.Pixels); // 入力は変えない
        Assert.NotSame(image.Pixels, result.Pixels);
    }

    [Theory] // E32: はみ出す位置
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(5, 0)]   // 幅 3 を x = 5 に置くと右にはみ出す
    [InlineData(0, 4)]   // 高さ 2 を y = 4 に置くと下にはみ出す
    [InlineData(int.MaxValue, 0)]
    public void Apply_OutOfImage_Throws(int x, int y)
    {
        var image = TestBuffers.Distinct(7, 5);

        Assert.Throws<ArgumentOutOfRangeException>(() => Paste.Apply(image, Solid(3, 2, 0), x, y));
    }

    [Fact]
    public void Apply_PatchAsLargeAsImage_ReturnsPatch()
    {
        var patch = Solid(7, 5, 0xFF000000);

        Assert.Same(patch, Paste.Apply(TestBuffers.Distinct(7, 5), patch, 0, 0));
    }

    [Fact]
    public void Apply_DecidesOpacityFromResult()
    {
        var opaque = TestBuffers.DistinctOpaque(4, 4);
        var transparentPatch = Solid(1, 1, 0x00000000);
        var opaquePatch = Solid(2, 2, 0xFF336699);

        Assert.False(Paste.Apply(opaque, transparentPatch, 1, 1).IsOpaque);
        Assert.True(Paste.Apply(opaque, opaquePatch, 1, 1).IsOpaque);

        // 透過のある画像の、透過の部分を不透明な画素で置き換えたら不透明になる
        var oneTransparent = Paste.Apply(opaque, transparentPatch, 0, 0);
        Assert.True(Paste.Apply(oneTransparent, Solid(1, 1, 0xFF000000), 0, 0).IsOpaque);
    }

    /// <summary>全画素が同じ値（B・G・R・A をまとめた値）の画像</summary>
    internal static ImageBuffer Solid(int width, int height, uint bgra)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) BitConverter.TryWriteBytes(pixels.AsSpan(i, 4), bgra);
        return ImageBuffer.Create(width, height, pixels);
    }
}
