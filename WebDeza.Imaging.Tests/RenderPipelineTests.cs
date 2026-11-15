using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Geometry;
using WebDeza.Imaging.Operations;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

public class RenderPipelineTests
{
    private static readonly RgbColor White = new(255, 255, 255);

    private static ExportSettings Settings(OutputSize size, RgbColor? background = null)
        => new("jpeg", new Dictionary<string, ParameterValues>(), null, size, background ?? White);

    private static RenderPipeline Pipeline(params IImageFilter[] filters)
    {
        var registry = new FilterRegistry();
        foreach (var filter in filters) registry.Register(filter);
        return new RenderPipeline(registry);
    }

    // ---------------------------------------------------------------- E25

    [Fact]
    public void RenderForExport_4x3ImageWithInitialRecipe_EqualsWholeImageResized()
    {
        var source = TestBuffers.DistinctOpaque(2048, 1536);

        var result = Pipeline(new BasicAdjustFilter()).RenderForExport(
            source, EditRecipe.Initial, CropAspect.Free, Settings(OutputSize.ExactSize(1024, 768)), encoderSupportsAlpha: false);

        TestBuffers.AssertSameImage(Resize.Apply(source, 1024, 768), result);
    }

    // ---------------------------------------------------------------- E26

    [Fact]
    public void RenderForExport_TransparentImage_KeepsAlphaWhenFormatSupportsIt()
    {
        var source = HalfTransparent(400, 300);

        var result = Pipeline().RenderForExport(
            source, EditRecipe.Initial, CropAspect.Free, Settings(OutputSize.OriginalSize), encoderSupportsAlpha: true);

        Assert.False(result.IsOpaque);
        Assert.Same(source, result); // 何も変えない設定なら、元画像のまま
    }

    [Fact]
    public void RenderForExport_TransparentImage_IsFlattenedWithBackgroundWhenFormatCannotKeepAlpha()
    {
        var source = HalfTransparent(400, 300);
        var background = new RgbColor(10, 128, 250);

        var result = Pipeline().RenderForExport(
            source, EditRecipe.Initial, CropAspect.Free, Settings(OutputSize.OriginalSize, background), encoderSupportsAlpha: false);

        Assert.True(result.IsOpaque);
        TestBuffers.AssertSameImage(Flatten.Apply(source, background), result);
        Assert.Equal(new byte[] { 250, 128, 10, 255 }, result.Pixels[..4]); // 左上は透明だったので背景色
    }

    [Fact]
    public void RenderForExport_OpaqueImage_IsNotFlattened()
    {
        var opaque = TestBuffers.DistinctOpaque(40, 30);

        var result = Pipeline().RenderForExport(
            opaque, EditRecipe.Initial, CropAspect.Free, Settings(OutputSize.OriginalSize), encoderSupportsAlpha: false);

        Assert.Same(opaque, result);
    }

    // ---------------------------------------------------------------- 処理の順番

    [Fact]
    public void RenderForExport_AppliesOrientationThenCropThenResize()
    {
        var source = TestBuffers.Distinct(300, 200);
        var orientation = ImageOrientation.Identity.RotateRight().FlipHorizontally(); // 200×300 になる
        var frame = new PixelRect(20, 30, 120, 90);
        var recipe = EditRecipe.Initial with { Orientation = orientation, Crop = frame };

        var result = Pipeline().RenderForExport(
            source, recipe, CropAspect.Free, Settings(OutputSize.LongSideOf(60)), encoderSupportsAlpha: true);

        var expected = Resize.Apply(Crop.Apply(Orientation.Apply(source, orientation), frame), 60, 45);
        TestBuffers.AssertSameImage(expected, result);
    }

    [Fact]
    public void RenderForExport_FrameOutsideRules_IsClamped()
    {
        var source = TestBuffers.Distinct(300, 200);
        // 画像からはみ出す枠 → 規則に合わせて (180, 120) に寄せる
        var recipe = EditRecipe.Initial with { Crop = new PixelRect(250, 150, 120, 80) };

        var result = Pipeline().RenderForExport(
            source, recipe, CropAspect.Free, Settings(OutputSize.OriginalSize), encoderSupportsAlpha: true);

        TestBuffers.AssertSameImage(Crop.Apply(source, new PixelRect(180, 120, 120, 80)), result);
    }

    [Fact]
    public void RenderForExport_NullFrame_UsesInitialFrameOfRules()
    {
        // 6000×2000 相当の比率（600×200）を 4:3 で: 初期状態は (166, 0, 267, 200)
        var source = TestBuffers.Distinct(600, 200);

        var result = Pipeline().RenderForExport(
            source, EditRecipe.Initial, CropAspect.Fixed(4, 3), Settings(OutputSize.OriginalSize), encoderSupportsAlpha: true);

        TestBuffers.AssertSameImage(Crop.Apply(source, new PixelRect(166, 0, 267, 200)), result);
    }

    [Fact]
    public void RenderForExport_DoesNotModifySource()
    {
        var source = HalfTransparent(120, 90);
        var before = (byte[])source.Pixels.Clone();
        var recipe = EditRecipe.Initial
            .WithFilter("basic", ParameterValues.Empty.With("brightness", 40))
            with { Orientation = new ImageOrientation(90, true), Crop = new PixelRect(5, 5, 60, 80) };

        Pipeline(new BasicAdjustFilter()).RenderForExport(
            source, recipe, CropAspect.Free, Settings(OutputSize.LongSideOf(50)), encoderSupportsAlpha: false);

        Assert.Equal(before, source.Pixels);
    }

    // ---------------------------------------------------------------- フィルター

    [Fact]
    public void Filters_AreAppliedInRegistrationOrderAfterResizeWithNormalizedValues()
    {
        var log = new List<string>();
        var first = new RecordingFilter("first", log);
        var second = new RecordingFilter("second", log);
        var recipe = EditRecipe.Initial
            .WithFilter("second", ParameterValues.Empty.With("amount", 250))  // 範囲外 → 100 に丸めて渡す
            .WithFilter("first", ParameterValues.Empty.With("amount", 3).With("unknown", 1)); // 知らないキーは捨てて渡す

        var result = Pipeline(first, second).RenderForExport(
            TestBuffers.Distinct(400, 300), recipe, CropAspect.Free, Settings(OutputSize.LongSideOf(100)), encoderSupportsAlpha: true);

        Assert.Equal(new[] { "first:3:100x75", "second:100:100x75" }, log); // リサイズの後に、登録順で
        Assert.Equal(ParameterValues.Empty.With("amount", 3), first.LastValues);
        Assert.Equal(1.0, first.LastContext!.PixelScale); // 書き出しでは 1
        Assert.Equal((100, 75), (result.Width, result.Height));
    }

    [Fact]
    public void Filters_WithDefaultValues_AreNotCalled()
    {
        var log = new List<string>();
        var active = new RecordingFilter("active", log);
        var idle = new RecordingFilter("idle", log);
        var recipe = EditRecipe.Initial
            .WithFilter("active", ParameterValues.Empty.With("amount", 5))
            .WithFilter("idle", ParameterValues.Empty.With("amount", 0)); // 既定値（IsIdentity）

        Pipeline(idle, active).RenderForExport(
            TestBuffers.Distinct(40, 30), recipe, CropAspect.Free, Settings(OutputSize.OriginalSize), encoderSupportsAlpha: true);

        Assert.Equal(new[] { "active:5:40x30" }, log);
        Assert.Equal(0, idle.ApplyCount);
    }

    [Fact]
    public void Filters_NotInRecipeOrNotRegistered()
    {
        var log = new List<string>();
        var registered = new RecordingFilter("registered", log);
        // レシピに無いフィルターは既定値（呼ばれない）。登録されていないフィルターの値は使わない
        var recipe = EditRecipe.Initial.WithFilter("removed-plugin", ParameterValues.Empty.With("amount", 50));

        var source = TestBuffers.Distinct(40, 30);
        var result = Pipeline(registered).RenderForExport(
            source, recipe, CropAspect.Free, Settings(OutputSize.OriginalSize), encoderSupportsAlpha: true);

        Assert.Empty(log);
        Assert.Same(source, result);
    }

    [Fact]
    public void BasicAdjust_IsApplied()
    {
        var source = TestBuffers.Distinct(40, 30);
        var values = ParameterValues.Empty.With("saturation", -100);
        var recipe = EditRecipe.Initial.WithFilter("basic", values);
        var filter = new BasicAdjustFilter();

        var result = Pipeline(filter).RenderForExport(
            source, recipe, CropAspect.Free, Settings(OutputSize.OriginalSize), encoderSupportsAlpha: true);

        TestBuffers.AssertSameImage(filter.Apply(source, values.Normalize(filter.Parameters), FilterContext.ForExport()), result);
    }

    // ---------------------------------------------------------------- E27: 表示用の画像

    [Fact]
    public void RenderPreview_LargeImage_IsShrunkToMaxSideWithFullEditedSize()
    {
        var source = TestBuffers.Distinct(8000, 10);
        var recipe = EditRecipe.Initial with { Orientation = ImageOrientation.Identity.RotateRight() };

        var preview = Pipeline().RenderPreview(source, recipe, CropAspect.Free, Settings(OutputSize.OriginalSize), maxSide: 4096);

        // 向きを適用すると 10×8000。長辺 4096 に縮小: 10 × 4096 ÷ 8000 = 5.12 → 5
        Assert.Equal((5, 4096), (preview.Base.Width, preview.Base.Height));
        Assert.True(Math.Max(preview.Image.Width, preview.Image.Height) <= 4096);
        Assert.Equal((10, 8000), (preview.EditedWidth, preview.EditedHeight));
    }

    [Fact]
    public void RenderPreview_SmallImage_IsNotResizedOrCropped()
    {
        var source = TestBuffers.Distinct(300, 200);
        var recipe = EditRecipe.Initial with { Crop = new PixelRect(10, 10, 100, 100) };

        var preview = Pipeline().RenderPreview(source, recipe, CropAspect.Free, Settings(OutputSize.OriginalSize), maxSide: 4096);

        Assert.Same(source, preview.Base); // 切り抜かず、縮小もしない
        Assert.Same(source, preview.Image);
        Assert.Equal((300, 200), (preview.EditedWidth, preview.EditedHeight));
    }

    [Fact]
    public void RenderPreview_KeepsTransparency()
    {
        var source = HalfTransparent(400, 300);

        var preview = Pipeline().RenderPreview(source, EditRecipe.Initial, CropAspect.Free, Settings(OutputSize.OriginalSize), maxSide: 100);

        Assert.False(preview.Image.IsOpaque); // 背景色で塗らない（キャンバスが市松模様の上に描く）
    }

    [Fact]
    public void RenderPreview_FiltersBaseAndKeepsBaseUnfiltered()
    {
        var source = TestBuffers.Distinct(400, 300);
        var values = ParameterValues.Empty.With("brightness", 50);
        var recipe = EditRecipe.Initial.WithFilter("basic", values);
        var filter = new BasicAdjustFilter();

        var preview = Pipeline(filter).RenderPreview(source, recipe, CropAspect.Free, Settings(OutputSize.OriginalSize), maxSide: 200);

        TestBuffers.AssertSameImage(Resize.Apply(source, 200, 150), preview.Base);
        TestBuffers.AssertSameImage(
            filter.Apply(preview.Base, values.Normalize(filter.Parameters), FilterContext.ForExport()), preview.Image);
    }

    [Theory]
    [InlineData(OutputSizeMode.Original, 4.0)]   // 表示用の 1 px = 編集画像の 4 px = 書き出しの 4 px
    [InlineData(OutputSizeMode.LongSide, 2.0)]   // 書き出し倍率 1000 ÷ 2000 = 0.5 → 4 × 0.5
    public void RenderPreview_PassesPixelScaleOfPreviewToFilters(OutputSizeMode mode, double expectedScale)
    {
        var filter = new RecordingFilter("f", new List<string>());
        var recipe = EditRecipe.Initial.WithFilter("f", ParameterValues.Empty.With("amount", 1));
        var size = mode == OutputSizeMode.Original ? OutputSize.OriginalSize : OutputSize.LongSideOf(1000);

        Pipeline(filter).RenderPreview(TestBuffers.Distinct(2000, 1000), recipe, CropAspect.Free, Settings(size), maxSide: 500);

        Assert.Equal(expectedScale, filter.LastContext!.PixelScale, 10);
    }

    [Fact]
    public void FilterPreview_ReusesGivenBase()
    {
        // フィルターの値を変えたときは、Base にかけ直すだけ（元画像から作り直さない）
        var filter = new BasicAdjustFilter();
        var pipeline = Pipeline(filter);
        var first = pipeline.RenderPreview(TestBuffers.Distinct(400, 300), EditRecipe.Initial, CropAspect.Free,
            Settings(OutputSize.OriginalSize), maxSide: 200);
        var values = ParameterValues.Empty.With("contrast", 30);

        var second = pipeline.FilterPreview(first.Base, first.EditedWidth, first.EditedHeight,
            EditRecipe.Initial.WithFilter("basic", values), CropAspect.Free, Settings(OutputSize.OriginalSize));

        Assert.Same(first.Base, second.Base);
        Assert.Equal((400, 300), (second.EditedWidth, second.EditedHeight));
        TestBuffers.AssertSameImage(filter.Apply(first.Base, values.Normalize(filter.Parameters), FilterContext.ForExport()), second.Image);
    }

    // ---------------------------------------------------------------- 取り消し・引数

    [Fact]
    public void CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var source = TestBuffers.Distinct(40, 30);

        Assert.ThrowsAny<OperationCanceledException>(() => Pipeline().RenderForExport(
            source, EditRecipe.Initial, CropAspect.Free, Settings(OutputSize.OriginalSize), true, cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => Pipeline().RenderPreview(
            source, EditRecipe.Initial, CropAspect.Free, Settings(OutputSize.OriginalSize), 100, cts.Token));
    }

    [Fact]
    public void RenderPreview_NonPositiveMaxSide_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Pipeline().RenderPreview(
            TestBuffers.Distinct(4, 3), EditRecipe.Initial, CropAspect.Free, Settings(OutputSize.OriginalSize), 0));
    }

    // ---------------------------------------------------------------- 補助

    /// <summary>左 1/3 が透明、中 1/3 が半透明、右 1/3 が不透明</summary>
    private static ImageBuffer HalfTransparent(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            pixels[i] = (byte)(x % 256);
            pixels[i + 1] = (byte)(y % 256);
            pixels[i + 2] = 200;
            pixels[i + 3] = x < width / 3 ? (byte)0 : x < width * 2 / 3 ? (byte)128 : (byte)255;
        }
        return ImageBuffer.Create(width, height, pixels);
    }

    /// <summary>呼ばれた順番と値を記録するフィルター。"amount"（0〜100、既定 0）が 0 なら何もしない</summary>
    private sealed class RecordingFilter(string id, List<string> log) : IImageFilter
    {
        private static readonly ParameterDescriptor[] Descriptors =
        {
            new("amount", "量", ParameterKind.Integer, 0, Min: 0, Max: 100),
        };

        public string Id => id;
        public string DisplayName => id;
        public IReadOnlyList<ParameterDescriptor> Parameters => Descriptors;
        public int ApplyCount { get; private set; }
        public ParameterValues? LastValues { get; private set; }
        public FilterContext? LastContext { get; private set; }

        public bool IsIdentity(ParameterValues values) => values.Get<int>("amount") == 0;

        public ImageBuffer Apply(ImageBuffer source, ParameterValues values, FilterContext context)
        {
            ApplyCount++;
            LastValues = values;
            LastContext = context;
            log.Add($"{id}:{values.Get<int>("amount")}:{source.Width}x{source.Height}");
            return source;
        }
    }
}
