namespace WebDeza.Imaging.Tests;

public class ValueTypeTests
{
    [Theory]
    [InlineData(1024, 768, 4, 3)]
    [InlineData(1920, 1080, 16, 9)]
    [InlineData(1200, 630, 40, 21)]
    [InlineData(1080, 1080, 1, 1)]
    [InlineData(768, 1024, 3, 4)]
    [InlineData(3, 2, 3, 2)]     // これ以上約分できない
    [InlineData(7, 1, 7, 1)]
    public void AspectRatio_IsReduced(int width, int height, int expectedWidth, int expectedHeight)
    {
        var ratio = new AspectRatio(width, height);

        Assert.Equal(expectedWidth, ratio.Width);
        Assert.Equal(expectedHeight, ratio.Height);
    }

    [Fact]
    public void AspectRatio_SameRatio_IsEqual()
    {
        Assert.Equal(new AspectRatio(4, 3), new AspectRatio(1024, 768));
        Assert.Equal(new AspectRatio(4, 3).GetHashCode(), new AspectRatio(1024, 768).GetHashCode());
        Assert.NotEqual(new AspectRatio(4, 3), new AspectRatio(3, 4));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(4, 0)]
    [InlineData(-4, 3)]
    [InlineData(4, -3)]
    public void AspectRatio_NonPositive_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AspectRatio(width, height));
    }

    [Fact]
    public void AspectRatio_Default_IsZeroByZero()
    {
        // 「比率なし」を表す値として使う
        AspectRatio ratio = default;

        Assert.Equal(0, ratio.Width);
        Assert.Equal(0, ratio.Height);
    }

    [Fact]
    public void PixelRect_ComparesByValue()
    {
        Assert.Equal(new PixelRect(1666, 0, 2667, 2000), new PixelRect(1666, 0, 2667, 2000));
        Assert.NotEqual(new PixelRect(1666, 0, 2667, 2000), new PixelRect(1667, 0, 2667, 2000));
    }

    [Fact]
    public void RgbColor_ComparesByValue()
    {
        Assert.Equal(new RgbColor(255, 255, 255), new RgbColor(255, 255, 255));
        Assert.NotEqual(new RgbColor(255, 255, 255), new RgbColor(255, 255, 254));
    }
}
