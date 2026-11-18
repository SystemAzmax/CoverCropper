using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.InfrastructureTests;

/// <summary>画素の比べ方（左右反転・画素ごとの差・色の数）</summary>
public class PixelsTests
{
    /// <summary>左半分が黒、右半分が白の 4×2</summary>
    private static Pixels HalfBlack()
    {
        var px = new byte[4 * 2 * 4];
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
            {
                int i = (y * 4 + x) * 4;
                byte v = x < 2 ? (byte)0 : (byte)255;
                px[i] = px[i + 1] = px[i + 2] = v;
                px[i + 3] = 255;
            }
        return new Pixels(4, 2, px);
    }

    [Fact]
    public void MirrorHorizontal_SwapsLeftAndRight()
    {
        var mirrored = HalfBlack().MirrorHorizontal();

        Assert.Equal(255, mirrored.Bgra[0]);
        Assert.Equal(0, mirrored.Bgra[3 * 4]);
        Assert.Equal(0, Pixels.PixelDiff(HalfBlack(), mirrored.MirrorHorizontal()));
    }

    [Fact]
    public void PixelDiff_AveragesColorDifference()
    {
        var a = HalfBlack();

        Assert.Equal(0, Pixels.PixelDiff(a, HalfBlack()));
        Assert.Equal(255, Pixels.PixelDiff(a, a.MirrorHorizontal()));
    }

    /// <summary>3×2 で、画素ごとに違う値（B に番号）</summary>
    private static Pixels Numbered()
    {
        var px = new byte[3 * 2 * 4];
        for (int i = 0; i < 6; i++) px[i * 4] = (byte)(i + 1);
        return new Pixels(3, 2, px);
    }

    private static int[] Blue(Pixels p) => Enumerable.Range(0, p.Width * p.Height).Select(i => (int)p.Bgra[i * 4]).ToArray();

    [Fact]
    public void MirrorVertical_SwapsTopAndBottom()
    {
        // 1 2 3 / 4 5 6 → 4 5 6 / 1 2 3
        Assert.Equal([4, 5, 6, 1, 2, 3], Blue(Numbered().MirrorVertical()));
    }

    [Fact]
    public void RotateRight_TurnsClockwise()
    {
        // 1 2 3 / 4 5 6 を右に 90° → 4 1 / 5 2 / 6 3
        var r = Numbered().RotateRight();
        Assert.Equal((2, 3), (r.Width, r.Height));
        Assert.Equal([4, 1, 5, 2, 6, 3], Blue(r));
    }

    [Fact]
    public void RegionDiff_OnlyInsideRegion()
    {
        var a = HalfBlack();
        var b = a.MirrorHorizontal();

        Assert.Equal(255, Pixels.RegionDiff(a, b, 0, 0, 1, 2));
        Assert.Equal(255, Pixels.RegionDiff(a, b, 0, 0, 4, 2));
        Assert.Equal(0, Pixels.RegionDiff(a, a, 0, 0, 4, 2));
        // 範囲は絵の中に切り詰める
        Assert.Equal(255, Pixels.RegionDiff(a, b, -10, -10, 100, 100));
    }

    [Fact]
    public void Chroma_GrayIsZero()
    {
        Assert.Equal(0, HalfBlack().Chroma());
        var red = new Pixels(1, 1, [0, 0, 200, 255]);
        Assert.Equal(200, red.Chroma());
    }

    [Fact]
    public void CountColor_CountsWithinTolerance()
    {
        var px = new byte[3 * 4];
        // BGRA: #3A96DD・少しずれた色・白
        px[0] = 0xDD; px[1] = 0x96; px[2] = 0x3A;
        px[4] = 0xDD - 5; px[5] = 0x96 + 5; px[6] = 0x3A;
        px[8] = px[9] = px[10] = 255;
        var p = new Pixels(3, 1, px);

        Assert.Equal(2, p.CountColor(0x3A, 0x96, 0xDD));
        Assert.Equal(1, p.CountColor(0x3A, 0x96, 0xDD, tolerance: 1));
    }
}
