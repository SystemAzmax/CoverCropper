using WebDeza.Imaging.Operations;

namespace WebDeza.Imaging.Tests;

public class ResizeTests
{
    // ---------------------------------------------------------------- E05: 大きさ・単色・等倍

    [Theory]
    [InlineData(400, 300, 100, 75)]   // 縮小
    [InlineData(80, 60, 200, 150)]    // 拡大
    [InlineData(300, 100, 100, 200)]  // 横は縮小、縦は拡大
    [InlineData(64, 48, 1, 1)]        // 1 画素まで縮小
    [InlineData(1, 1, 20, 10)]        // 1 画素から拡大
    [InlineData(200, 100, 199, 100)]  // 幅だけ変える
    public void Apply_ProducesRequestedSize(int srcWidth, int srcHeight, int width, int height)
    {
        foreach (var source in new[] { Pattern(srcWidth, srcHeight), Pattern(srcWidth, srcHeight, withAlpha: true) })
        {
            var result = Resize.Apply(source, width, height);

            Assert.Equal(width, result.Width);
            Assert.Equal(height, result.Height);
            Assert.Equal(width * height * 4, result.Pixels.Length);
        }
    }

    [Theory]
    [InlineData(300, 200, 115, 77)]   // 縮小
    [InlineData(60, 40, 173, 115)]    // 拡大
    [InlineData(3000, 20, 1152, 8)]   // 大きな縮小率（CoverCropper のテストと同じ色）
    public void Apply_SolidColor_KeepsColor(int srcWidth, int srcHeight, int width, int height)
    {
        var source = Solid(srcWidth, srcHeight, b: 250, g: 128, r: 10, a: 255);

        var result = Resize.Apply(source, width, height);

        Assert.True(result.IsOpaque);
        AssertAllPixels(result, b: 250, g: 128, r: 10, a: 255);
    }

    [Theory]
    [InlineData(300, 200, 115, 77)]
    [InlineData(60, 40, 173, 115)]
    public void Apply_SolidTranslucentColor_KeepsColorAndAlpha(int srcWidth, int srcHeight, int width, int height)
    {
        var source = Solid(srcWidth, srcHeight, b: 250, g: 128, r: 10, a: 100);

        var result = Resize.Apply(source, width, height);

        Assert.False(result.IsOpaque);
        AssertAllPixels(result, b: 250, g: 128, r: 10, a: 100);
    }

    [Fact]
    public void Apply_SameSize_ReturnsInput()
    {
        var opaque = Pattern(120, 90);
        var translucent = Pattern(120, 90, withAlpha: true);

        Assert.Same(opaque, Resize.Apply(opaque, 120, 90));
        Assert.Same(translucent, Resize.Apply(translucent, 120, 90));
    }

    [Fact]
    public void Apply_DoesNotModifyInput()
    {
        var source = Pattern(120, 90, withAlpha: true);
        var before = (byte[])source.Pixels.Clone();

        var result = Resize.Apply(source, 60, 45);

        Assert.NotSame(source.Pixels, result.Pixels);
        Assert.Equal(before, source.Pixels);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public void Apply_NonPositiveSize_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Resize.Apply(Pattern(8, 8), width, height));
    }

    [Fact]
    public void Apply_TooLargeSize_ThrowsImageTooLargeException()
    {
        // 出力の配列を作る前に検証するので、メモリは使わない
        Assert.Throws<ImageTooLargeException>(() => Resize.Apply(Pattern(8, 8), 32768, 32768));
    }

    [Fact]
    public void Apply_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Resize.Apply(Pattern(64, 48), 32, 24, cts.Token));
    }

    [Fact]
    public void Apply_IsDeterministic()
    {
        // 行を並列に処理しても、毎回同じ結果になる
        var source = Pattern(640, 480);

        var first = Resize.Apply(source, 213, 160);
        var second = Resize.Apply(source, 213, 160);

        TestBuffers.AssertSameImage(first, second);
    }

    // ---------------------------------------------------------------- E06: 透過

    [Theory]
    [InlineData(200, 100, 50, 25)]    // 1/4 に縮小
    [InlineData(200, 100, 37, 19)]    // 割り切れない縮小率
    [InlineData(200, 100, 100, 50)]   // 1/2 に縮小
    [InlineData(40, 20, 130, 65)]     // 拡大
    public void Apply_TransparentBlackNextToOpaqueWhite_WhiteDoesNotTurnGray(
        int srcWidth, int srcHeight, int width, int height)
    {
        // 左半分が透明な黒 (0,0,0,0)、右半分が不透明な白
        var source = SplitImage(srcWidth, srcHeight,
            left: (0, 0, 0, 0), right: (255, 255, 255, 255));

        var result = Resize.Apply(source, width, height);

        int visible = 0, translucent = 0;
        for (int i = 0; i < result.Pixels.Length; i += 4)
        {
            byte alpha = result.Pixels[i + 3];
            if (alpha == 0) continue;
            visible++;
            if (alpha < 255) translucent++;

            // 少しでも見える画素は白のまま（透明な部分の黒が混ざって灰色にならない）
            if (result.Pixels[i] != 255 || result.Pixels[i + 1] != 255 || result.Pixels[i + 2] != 255)
                Assert.Fail($"画素 {i / 4} が白ではありません: B={result.Pixels[i]} G={result.Pixels[i + 1]} "
                    + $"R={result.Pixels[i + 2]} A={alpha}");
        }

        // 左端は透明、右端は不透明な白のまま
        Assert.Equal(0u, TestBuffers.PixelAt(result, 0, height / 2));
        Assert.Equal(0xFFFFFFFFu, TestBuffers.PixelAt(result, width - 1, height / 2));
        Assert.InRange(visible, 1, width * height - 1);
        // 境目がちょうど画素の境に来ない縮小率では、半透明の画素ができる（アルファはなめらかに変わる）
        if (width == 37) Assert.True(translucent > 0, "境目に半透明の画素がありません");
    }

    [Fact]
    public void Apply_TransparentPixelsWithColor_ColorDoesNotBleed()
    {
        // 透明な部分が派手な色（緑）を持っていても、見える部分（赤）には混ざらない
        var source = SplitImage(200, 100, left: (0, 255, 0, 0), right: (0, 0, 255, 255));

        var result = Resize.Apply(source, 37, 19);

        for (int i = 0; i < result.Pixels.Length; i += 4)
        {
            if (result.Pixels[i + 3] == 0)
            {
                // 透明になった画素は、色も 0 にそろえる
                Assert.Equal(0, result.Pixels[i] | result.Pixels[i + 1] | result.Pixels[i + 2]);
                continue;
            }
            Assert.Equal(0, result.Pixels[i]);       // B
            Assert.Equal(0, result.Pixels[i + 1]);   // G（にじんでいない）
            Assert.Equal(255, result.Pixels[i + 2]); // R
        }
    }

    [Fact]
    public void Apply_AlphaGradient_KeepsColorAndInterpolatesAlpha()
    {
        // 色は一定で、アルファだけが左から右へ 0 → 255 と変わる画像
        const int width = 256, height = 32;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            pixels[i] = 40;
            pixels[i + 1] = 120;
            pixels[i + 2] = 200;
            pixels[i + 3] = (byte)x;
        }
        var source = ImageBuffer.Create(width, height, pixels);

        var result = Resize.Apply(source, 64, 8);

        int previousAlpha = -1;
        for (int x = 0; x < result.Width; x++)
        {
            int i = (4 * result.Width + x) * 4;
            int alpha = result.Pixels[i + 3];
            Assert.True(alpha >= previousAlpha, $"x={x} でアルファが減っています（{previousAlpha} → {alpha}）");
            previousAlpha = alpha;
            if (alpha == 0) continue;

            Assert.Equal(40, result.Pixels[i]);
            Assert.Equal(120, result.Pixels[i + 1]);
            Assert.Equal(200, result.Pixels[i + 2]);
            // 4 画素ずつの平均に近い値になる（x*4 〜 x*4+3 の平均は x*4 + 1.5）
            Assert.InRange(alpha, x * 4 - 1, x * 4 + 4);
        }
    }

    [Fact]
    public void Apply_OpaquePixelsThroughAlphaPath_MatchOpaquePathWithinOne()
    {
        // 同じ不透明な画素を、不透明用の計算と透過用の計算の両方に通す。
        // 透過用は「色 × アルファ」をアルファで割り戻すので、丸めの分（1 以内）だけ違いうる
        var opaque = Pattern(320, 240);
        var sameButNotMarkedOpaque = ImageBuffer.Create(320, 240, (byte[])opaque.Pixels.Clone(), isOpaque: false);

        foreach (var (width, height) in new[] { (107, 80), (500, 375) })
        {
            var expected = Resize.Apply(opaque, width, height);
            var actual = Resize.Apply(sameButNotMarkedOpaque, width, height);

            int maxDifference = 0;
            for (int i = 0; i < expected.Pixels.Length; i++)
                maxDifference = Math.Max(maxDifference, Math.Abs(expected.Pixels[i] - actual.Pixels[i]));
            Assert.True(maxDifference <= 1, $"{width}×{height}: 差の最大が {maxDifference} です");
        }
    }

    // ---------------------------------------------------------------- E07: CoverCropper の基準ファイルと一致

    [Theory]
    [InlineData("landscape", 3000, 1000, 2304, 768)]    // 横長の縮小
    [InlineData("portrait", 1500, 2000, 1024, 1365)]    // 縦長の縮小
    [InlineData("ratio4x3", 2048, 1536, 1024, 768)]     // ちょうど 1/2
    [InlineData("small", 800, 600, 1024, 768)]          // 拡大
    [InlineData("wide_small", 500, 100, 3840, 768)]     // 大きな拡大
    [InlineData("transparent", 1600, 1200, 1024, 768)]  // 白で塗った後の画像
    [InlineData("orientation6", 1200, 1600, 1024, 1365)] // 回転した後の画像
    [InlineData("palette", 1600, 900, 1365, 768)]       // パレット画像を読んだもの
    [InlineData("gray16", 1000, 1500, 1024, 1536)]      // 16 bit グレースケールを読んだもの
    public void Apply_OpaqueImage_MatchesCoverCropperBaseline(
        string name, int loadedWidth, int loadedHeight, int resizedWidth, int resizedHeight)
    {
        // loaded.png = 置き換える前の CoverCropper の ImageLoader の結果（不透明）
        // resized.png = それを置き換える前の ImageResizer でリサイズした結果
        var loaded = WpfImageFiles.Load(WpfImageFiles.BaselineExpectedPath($"{name}.loaded.png"));
        var expected = WpfImageFiles.Load(WpfImageFiles.BaselineExpectedPath($"{name}.resized.png"));
        Assert.Equal((loadedWidth, loadedHeight), (loaded.Width, loaded.Height));
        Assert.Equal((resizedWidth, resizedHeight), (expected.Width, expected.Height));
        Assert.True(loaded.IsOpaque);

        var actual = Resize.Apply(loaded, resizedWidth, resizedHeight);

        Assert.Equal((resizedWidth, resizedHeight), (actual.Width, actual.Height));
        Assert.True(actual.IsOpaque);

        long differences = 0;
        int maxDifference = 0;
        for (int i = 0; i < expected.Pixels.Length; i++)
        {
            int d = Math.Abs(expected.Pixels[i] - actual.Pixels[i]);
            if (d == 0) continue;
            differences++;
            maxDifference = Math.Max(maxDifference, d);
        }
        Assert.True(differences == 0, $"{name}: {differences} 個の値が基準と違います（差の最大 {maxDifference}）");
    }

    // ---------------------------------------------------------------- 補助

    /// <summary>なめらかな変化と細かい変化（市松・斜め線）を持つ模様。withAlpha のときはアルファも場所で変える</summary>
    private static ImageBuffer Pattern(int width, int height, bool withAlpha = false)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            pixels[i] = (byte)(((x / 4 + y / 4) & 1) == 0 ? 40 : 215);
            pixels[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
            pixels[i + 2] = (byte)((x + y) % 23 == 0 ? 255 : x * 255 / Math.Max(1, width - 1));
            pixels[i + 3] = withAlpha ? (byte)((x * 3 + y * 5) % 256) : (byte)255;
        }
        return ImageBuffer.Create(width, height, pixels);
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

    /// <summary>左半分と右半分を、それぞれ 1 色（B, G, R, A）で塗った画像</summary>
    private static ImageBuffer SplitImage(int width, int height,
        (byte B, byte G, byte R, byte A) left, (byte B, byte G, byte R, byte A) right)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var (b, g, r, a) = x < width / 2 ? left : right;
            int i = (y * width + x) * 4;
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }
        return ImageBuffer.Create(width, height, pixels);
    }

    private static void AssertAllPixels(ImageBuffer image, byte b, byte g, byte r, byte a)
    {
        for (int i = 0; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i] != b || image.Pixels[i + 1] != g || image.Pixels[i + 2] != r || image.Pixels[i + 3] != a)
                Assert.Fail($"画素 {i / 4} が ({image.Pixels[i]}, {image.Pixels[i + 1]}, {image.Pixels[i + 2]}, "
                    + $"{image.Pixels[i + 3]}) で、期待した ({b}, {g}, {r}, {a}) と違います");
        }
    }
}
