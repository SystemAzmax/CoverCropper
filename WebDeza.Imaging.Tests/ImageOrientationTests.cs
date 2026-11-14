using WebDeza.Imaging.Geometry;

namespace WebDeza.Imaging.Tests;

public class ImageOrientationTests
{
    [Theory] // E01: 設計書 5.7 の表
    [InlineData(0, false, 1)]
    [InlineData(90, false, 6)]
    [InlineData(180, false, 3)]
    [InlineData(270, false, 8)]
    [InlineData(0, true, 2)]
    [InlineData(90, true, 7)]
    [InlineData(180, true, 4)]
    [InlineData(270, true, 5)]
    public void ToExif_MatchesDesignTable(int rotation, bool flipHorizontal, int expectedExif)
    {
        var orientation = new ImageOrientation(rotation, flipHorizontal);

        Assert.Equal(expectedExif, orientation.ToExif());
        Assert.Equal(orientation, ImageOrientation.FromExif(expectedExif));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(-1)]
    public void FromExif_OneOrOutOfRange_IsIdentity(int exifOrientation)
    {
        Assert.Equal(ImageOrientation.Identity, ImageOrientation.FromExif(exifOrientation));
    }

    [Fact] // E02
    public void Operations_RepeatedToFullCycle_ReturnToStart()
    {
        foreach (var start in AllOrientations())
        {
            Assert.Equal(start, start.RotateRight().RotateRight().RotateRight().RotateRight());
            Assert.Equal(start, start.RotateLeft().RotateLeft().RotateLeft().RotateLeft());
            Assert.Equal(start, start.FlipHorizontally().FlipHorizontally());
            Assert.Equal(start, start.FlipVertically().FlipVertically());
        }
    }

    [Fact]
    public void Operations_FromIdentity_GiveExpectedOrientation()
    {
        var identity = ImageOrientation.Identity;

        Assert.Equal(new ImageOrientation(90, false), identity.RotateRight());
        Assert.Equal(new ImageOrientation(270, false), identity.RotateLeft());
        Assert.Equal(new ImageOrientation(0, true), identity.FlipHorizontally());
        Assert.Equal(new ImageOrientation(180, true), identity.FlipVertically());
    }

    [Fact]
    public void RotateRightThenLeft_Cancels()
    {
        foreach (var start in AllOrientations())
        {
            Assert.Equal(start, start.RotateRight().RotateLeft());
            Assert.Equal(start, start.RotateLeft().RotateRight());
        }
    }

    [Fact]
    public void Identity_IsDefault()
    {
        Assert.Equal(default, ImageOrientation.Identity);
        Assert.True(ImageOrientation.Identity.IsIdentity);
        Assert.Equal(1, default(ImageOrientation).ToExif());
        Assert.Single(AllOrientations(), o => o.IsIdentity);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(90, true)]
    [InlineData(180, false)]
    [InlineData(270, true)]
    public void SwapsDimensions_OnlyFor90And270(int rotation, bool expected)
    {
        Assert.Equal(expected, new ImageOrientation(rotation, false).SwapsDimensions);
        Assert.Equal(expected, new ImageOrientation(rotation, true).SwapsDimensions);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(360)]
    [InlineData(-90)]
    [InlineData(1)]
    public void Constructor_InvalidRotation_Throws(int rotation)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageOrientation(rotation, false));
    }

    internal static IEnumerable<ImageOrientation> AllOrientations()
    {
        foreach (bool flip in new[] { false, true })
        foreach (int rotation in new[] { 0, 90, 180, 270 })
            yield return new ImageOrientation(rotation, flip);
    }
}
