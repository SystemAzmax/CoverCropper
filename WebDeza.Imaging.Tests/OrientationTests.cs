using WebDeza.Imaging.Geometry;

namespace WebDeza.Imaging.Tests;

public class OrientationTests
{
    /// <summary>4 つの操作と、その操作 1 回分の「差分の変換」（設計書 5.8）</summary>
    private static readonly (Func<ImageOrientation, ImageOrientation> Operate, ImageOrientation Delta)[] Operations =
    {
        (o => o.RotateRight(), new ImageOrientation(90, false)),
        (o => o.RotateLeft(), new ImageOrientation(270, false)),
        (o => o.FlipHorizontally(), new ImageOrientation(0, true)),
        (o => o.FlipVertically(), new ImageOrientation(180, true)),
    };

    [Fact] // E03
    public void RandomOperationSequences_ComposedOrientationMatchesStepByStep()
    {
        var source = TestBuffers.Distinct(3, 2);
        var random = new Random(20261114);

        for (int i = 0; i < 1000; i++)
        {
            int length = random.Next(1, 11);
            var stepByStep = source;
            var composed = ImageOrientation.Identity;
            var log = new List<int>();

            for (int k = 0; k < length; k++)
            {
                int op = random.Next(Operations.Length);
                log.Add(op);
                stepByStep = Orientation.Apply(stepByStep, Operations[op].Delta);
                composed = Operations[op].Operate(composed);
            }

            var atOnce = Orientation.Apply(source, composed);

            Assert.True(
                (stepByStep.Width, stepByStep.Height) == (atOnce.Width, atOnce.Height)
                && stepByStep.Pixels.AsSpan().SequenceEqual(atOnce.Pixels),
                $"操作の列 [{string.Join(", ", log)}] で、1 操作ずつ適用した画像と、合成した向き {composed} の画像が違います");
        }
    }

    [Fact]
    public void EveryOperationFromEveryOrientation_MatchesApplyingDelta()
    {
        // E03 の総当たり版: 8 通りの向き × 4 つの操作
        var source = TestBuffers.Distinct(3, 2);

        foreach (var start in ImageOrientationTests.AllOrientations())
        foreach (var (operate, delta) in Operations)
        {
            var stepByStep = Orientation.Apply(Orientation.Apply(source, start), delta);
            var atOnce = Orientation.Apply(source, operate(start));

            TestBuffers.AssertSameImage(stepByStep, atOnce);
        }
    }

    [Theory] // CoverCropper の ImageLoaderTests と同じ期待値（3×2 の画像の左上に来る画素）
    [InlineData(2, 3, 2, 2, 0)] // 左右反転: (0,0) ← (2,0)
    [InlineData(3, 3, 2, 2, 1)] // 180°: (0,0) ← (2,1)
    [InlineData(4, 3, 2, 0, 1)] // 上下反転: (0,0) ← (0,1)
    [InlineData(5, 2, 3, 0, 0)] // 転置: (0,0) ← (0,0)
    [InlineData(6, 2, 3, 0, 1)] // 時計回り: (0,0) ← 左下 (0,1)
    [InlineData(7, 2, 3, 2, 1)] // 反転置: (0,0) ← (2,1)
    [InlineData(8, 2, 3, 2, 0)] // 反時計回り: (0,0) ← 右上 (2,0)
    public void ApplyExif_MapsTopLeftPixelCorrectly(int exif, int expectedWidth, int expectedHeight, int srcX, int srcY)
    {
        var source = TestBuffers.Distinct(3, 2);

        var result = Orientation.ApplyExif(source, exif);

        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
        Assert.Equal(TestBuffers.PixelAt(source, srcX, srcY), TestBuffers.PixelAt(result, 0, 0));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(1, 1)]
    [InlineData(1, 5)]
    [InlineData(5, 1)]
    [InlineData(257, 131)] // 行を並列に処理しても同じ結果になること
    public void ApplyExif_AllPixelsMatchCoverCropperMapping(int width, int height)
    {
        var source = TestBuffers.Distinct(width, height);

        for (int exif = 2; exif <= 8; exif++)
        {
            var result = Orientation.ApplyExif(source, exif);

            bool swap = exif >= 5;
            Assert.Equal(swap ? height : width, result.Width);
            Assert.Equal(swap ? width : height, result.Height);

            for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                // CoverCropper の ImageLoader.ApplyOrientation と同じ対応
                (int sx, int sy) = exif switch
                {
                    2 => (width - 1 - x, y),
                    3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y),
                    5 => (y, x),
                    6 => (y, height - 1 - x),
                    7 => (width - 1 - y, height - 1 - x),
                    _ => (width - 1 - y, x),
                };
                if (TestBuffers.PixelAt(source, sx, sy) != TestBuffers.PixelAt(result, x, y))
                    Assert.Fail($"EXIF {exif}・{width}×{height}: 出力 ({x}, {y}) が入力 ({sx}, {sy}) と違います");
            }
        }
    }

    [Fact]
    public void Apply_RotateRight_MovesPixelsClockwise()
    {
        // 3×2 の画像     右に 90° 回転すると 2×3
        //   a b c          d a
        //   d e f          e b
        //                  f c
        var source = TestBuffers.Distinct(3, 2);

        var result = Orientation.Apply(source, ImageOrientation.Identity.RotateRight());

        Assert.Equal((2, 3), (result.Width, result.Height));
        Assert.Equal(TestBuffers.PixelAt(source, 0, 1), TestBuffers.PixelAt(result, 0, 0)); // d
        Assert.Equal(TestBuffers.PixelAt(source, 0, 0), TestBuffers.PixelAt(result, 1, 0)); // a
        Assert.Equal(TestBuffers.PixelAt(source, 2, 1), TestBuffers.PixelAt(result, 0, 2)); // f
        Assert.Equal(TestBuffers.PixelAt(source, 2, 0), TestBuffers.PixelAt(result, 1, 2)); // c
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(9)]
    public void ApplyExif_OneOrOutOfRange_ReturnsInput(int exif)
    {
        var source = TestBuffers.Distinct(3, 2);

        Assert.Same(source, Orientation.ApplyExif(source, exif));
    }

    [Fact]
    public void Apply_Identity_ReturnsInput()
    {
        var source = TestBuffers.Distinct(3, 2);

        Assert.Same(source, Orientation.Apply(source, ImageOrientation.Identity));
    }

    [Fact]
    public void Apply_DoesNotModifyInput()
    {
        var source = TestBuffers.Distinct(4, 3);
        var before = (byte[])source.Pixels.Clone();

        foreach (var orientation in ImageOrientationTests.AllOrientations())
        {
            var result = Orientation.Apply(source, orientation);
            if (!orientation.IsIdentity) Assert.NotSame(source.Pixels, result.Pixels);
        }

        Assert.Equal(before, source.Pixels);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_KeepsIsOpaque(bool opaque)
    {
        var pixels = new byte[4 * 3 * 4];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = opaque ? (byte)255 : (byte)100;
        var source = ImageBuffer.Create(4, 3, pixels);

        foreach (var orientation in ImageOrientationTests.AllOrientations())
            Assert.Equal(opaque, Orientation.Apply(source, orientation).IsOpaque);
    }

    [Fact]
    public void Apply_CancelledToken_Throws()
    {
        var source = TestBuffers.Distinct(3, 2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => Orientation.Apply(source, new ImageOrientation(90, false), cts.Token));
    }
}
