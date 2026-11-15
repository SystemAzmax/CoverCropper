using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Operations;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

public class RegionFilterTests
{
    private static readonly ParameterValues Brighter =
        ParameterValues.Empty.With(BasicAdjustFilter.BrightnessKey, 40L).With(BasicAdjustFilter.SaturationKey, -30L);

    private static FilterContext Context => FilterContext.ForExport();

    // ---------------------------------------------------------------- E33

    [Fact]
    public void NoRegion_SameAsWholeImage()
    {
        var image = TestBuffers.Distinct(20, 15);
        var filter = new BasicAdjustFilter();

        var result = RegionFilter.Apply(image, null, filter, Brighter, Context);

        TestBuffers.AssertSameImage(filter.Apply(image, Brighter.Normalize(filter.Parameters), Context), result);
    }

    [Theory]
    [InlineData(3, 2, 8, 5)]    // 中ほど
    [InlineData(0, 0, 20, 1)]   // 上の 1 行
    [InlineData(19, 0, 1, 15)]  // 右の 1 列
    [InlineData(12, 10, 8, 5)]  // 右下の角まで
    public void Region_OnlyInsideChanges(int x, int y, int width, int height)
    {
        var image = TestBuffers.Distinct(20, 15);
        var original = (byte[])image.Pixels.Clone();
        var filter = new BasicAdjustFilter();
        var region = new PixelRect(x, y, width, height);

        var result = RegionFilter.Apply(image, region, filter, Brighter, Context);

        // 範囲の中は、切り出してフィルターをかけた結果と一致する
        var expectedInside = filter.Apply(Crop.Apply(image, region), Brighter.Normalize(filter.Parameters), Context);
        for (int py = 0; py < 15; py++)
        for (int px = 0; px < 20; px++)
        {
            bool inside = px >= x && px < x + width && py >= y && py < y + height;
            uint expected = inside
                ? TestBuffers.PixelAt(expectedInside, px - x, py - y)
                : TestBuffers.PixelAt(image, px, py); // 範囲の外は全画素そのまま
            Assert.Equal(expected, TestBuffers.PixelAt(result, px, py));
        }
        Assert.Equal(original, image.Pixels); // 入力は変えない
    }

    [Fact]
    public void Region_FilterSeesOnlyPixelsInsideRegion()
    {
        // 左半分が黒・右半分が白の画像で、左半分だけを範囲にして「平均の色で塗る」フィルターをかける
        var image = Halves(10, 4);
        var filter = new AverageFilter();

        var result = RegionFilter.Apply(image, new PixelRect(0, 0, 5, 4), filter, ParameterValues.Empty, Context);

        // 範囲の外（白）の画素は材料にならないので、範囲の中は黒のまま
        Assert.Equal(new PixelRect(0, 0, 5, 4), filter.LastSize);
        Assert.Equal(0xFF000000u, TestBuffers.PixelAt(result, 0, 0));
        Assert.Equal(0xFF000000u, TestBuffers.PixelAt(result, 4, 3));
        Assert.Equal(0xFFFFFFFFu, TestBuffers.PixelAt(result, 5, 0));
    }

    [Fact]
    public void FilterReturningDifferentSize_Throws()
    {
        var image = TestBuffers.Distinct(10, 8);
        var filter = new ResizingFilter();

        Assert.Throws<InvalidOperationException>(() =>
            RegionFilter.Apply(image, null, filter, ParameterValues.Empty, Context));
        Assert.Throws<InvalidOperationException>(() =>
            RegionFilter.Apply(image, new PixelRect(1, 1, 4, 4), filter, ParameterValues.Empty, Context));
    }

    // ---------------------------------------------------------------- 追加

    [Fact]
    public void Identity_ReturnsInputWithoutCallingFilter()
    {
        var image = TestBuffers.Distinct(10, 8);
        var filter = new AverageFilter { Identity = true };

        Assert.Same(image, RegionFilter.Apply(image, null, filter, ParameterValues.Empty, Context));
        Assert.Same(image, RegionFilter.Apply(image, new PixelRect(1, 1, 3, 3), filter, ParameterValues.Empty, Context));
        Assert.Equal(0, filter.CallCount);
    }

    [Fact]
    public void Values_AreNormalizedBeforePassingToFilter()
    {
        var image = TestBuffers.Distinct(10, 8);
        var filter = new BasicAdjustFilter();
        // 範囲外の値は記述の範囲に丸めてから渡す（明るさ 500 → 100）
        var tooLarge = ParameterValues.Empty.With(BasicAdjustFilter.BrightnessKey, 500L);
        var clamped = ParameterValues.Empty.With(BasicAdjustFilter.BrightnessKey, 100L);

        var result = RegionFilter.Apply(image, null, filter, tooLarge, Context);

        TestBuffers.AssertSameImage(filter.Apply(image, clamped.Normalize(filter.Parameters), Context), result);
    }

    [Theory]
    [InlineData(-1, 0, 3, 3)]
    [InlineData(8, 0, 3, 3)]   // 右にはみ出す
    [InlineData(0, 0, 0, 3)]   // 幅が 0
    public void RegionOutsideImage_Throws(int x, int y, int width, int height)
    {
        var image = TestBuffers.Distinct(10, 8);

        // 既定値（何もしない値）でも、範囲の誤りは知らせる
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RegionFilter.Apply(image, new PixelRect(x, y, width, height), new BasicAdjustFilter(), ParameterValues.Empty, Context));
    }

    [Fact]
    public void RegionAsLargeAsImage_SameAsNoRegion()
    {
        var image = TestBuffers.Distinct(10, 8);
        var filter = new BasicAdjustFilter();

        var whole = RegionFilter.Apply(image, null, filter, Brighter, Context);
        var region = RegionFilter.Apply(image, new PixelRect(0, 0, 10, 8), filter, Brighter, Context);

        TestBuffers.AssertSameImage(whole, region);
    }

    [Fact]
    public void Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => RegionFilter.Apply(
            TestBuffers.Distinct(4, 4), null, new BasicAdjustFilter(), Brighter, new FilterContext(1, cts.Token)));
    }

    // ---------------------------------------------------------------- テスト用のフィルター

    /// <summary>左半分が不透明な黒、右半分が不透明な白の画像</summary>
    private static ImageBuffer Halves(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            byte v = x < width / 2 ? (byte)0 : (byte)255;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
            pixels[i + 3] = 255;
        }
        return ImageBuffer.Create(width, height, pixels);
    }

    /// <summary>渡された画像の平均の色で塗る（範囲の外の画素が材料に混ざっていないかを確かめるため）</summary>
    private sealed class AverageFilter : IImageFilter
    {
        public bool Identity { get; init; }
        public int CallCount { get; private set; }
        public PixelRect LastSize { get; private set; }

        public string Id => "average";
        public string DisplayName => "平均";
        public IReadOnlyList<ParameterDescriptor> Parameters => Array.Empty<ParameterDescriptor>();
        public bool IsIdentity(ParameterValues values) => Identity;

        public ImageBuffer Apply(ImageBuffer source, ParameterValues values, FilterContext context)
        {
            CallCount++;
            LastSize = new PixelRect(0, 0, source.Width, source.Height);
            var sums = new long[4];
            for (int i = 0; i < source.Pixels.Length; i++) sums[i % 4] += source.Pixels[i];
            int count = source.Width * source.Height;
            var pixels = new byte[source.Pixels.Length];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(sums[i % 4] / count);
            return ImageBuffer.Create(source.Width, source.Height, pixels);
        }
    }

    /// <summary>大きさを変えてしまう誤ったフィルター</summary>
    private sealed class ResizingFilter : IImageFilter
    {
        public string Id => "resizing";
        public string DisplayName => "大きさを変える";
        public IReadOnlyList<ParameterDescriptor> Parameters => Array.Empty<ParameterDescriptor>();
        public bool IsIdentity(ParameterValues values) => false;
        public ImageBuffer Apply(ImageBuffer source, ParameterValues values, FilterContext context)
            => Resize.Apply(source, source.Width + 1, source.Height);
    }
}
