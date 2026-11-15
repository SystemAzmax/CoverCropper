using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Geometry;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

public class EditingModelTests
{
    private static ParameterValues Basic(int brightness, int contrast = 0)
        => ParameterValues.Empty.With("brightness", brightness).With("contrast", contrast);

    private static ParameterValues Quality(int quality) => ParameterValues.Empty.With("quality", quality);

    // ---------------------------------------------------------------- E24: EditRecipe

    [Fact]
    public void EditRecipe_SameContentInDifferentDictionaries_IsEqual()
    {
        var a = new EditRecipe(new ImageOrientation(90, false), new PixelRect(10, 20, 300, 200),
            new Dictionary<string, ParameterValues> { ["basic"] = Basic(30), ["mono"] = ParameterValues.Empty });
        // 別の辞書・足した順番も違う・値も別に作ったもの
        var b = new EditRecipe(new ImageOrientation(90, false), new PixelRect(10, 20, 300, 200),
            new Dictionary<string, ParameterValues> { ["mono"] = ParameterValues.Empty, ["basic"] = Basic(30) });

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void EditRecipe_DifferentContent_IsNotEqual()
    {
        var filters = new Dictionary<string, ParameterValues> { ["basic"] = Basic(30) };
        var recipe = new EditRecipe(ImageOrientation.Identity, new PixelRect(0, 0, 100, 100), filters);

        Assert.NotEqual(recipe, recipe with { Orientation = new ImageOrientation(0, true) });
        Assert.NotEqual(recipe, recipe with { Crop = new PixelRect(1, 0, 100, 100) });
        Assert.NotEqual(recipe, recipe with { Crop = null });
        Assert.NotEqual(recipe, recipe.WithFilter("basic", Basic(31)));
        Assert.NotEqual(recipe, recipe.WithFilter("mono", ParameterValues.Empty));
        Assert.NotEqual(recipe, recipe with { Filters = new Dictionary<string, ParameterValues>() });
        Assert.False(recipe.Equals(null));
    }

    [Fact]
    public void EditRecipe_Initial_EqualsNewEmptyRecipe()
    {
        var empty = new EditRecipe(ImageOrientation.Identity, null, new Dictionary<string, ParameterValues>());

        Assert.Equal(EditRecipe.Initial, empty);
        Assert.Equal(EditRecipe.Initial.GetHashCode(), empty.GetHashCode());
        Assert.Equal(ImageOrientation.Identity, EditRecipe.Initial.Orientation);
        Assert.Null(EditRecipe.Initial.Crop);
        Assert.Empty(EditRecipe.Initial.Filters);
    }

    [Fact]
    public void EditRecipe_NumbersAsIntegerOrReal_AreEqual()
    {
        // 設定ファイルから読み直した値（85.0）と、画面で入れた値（85）が同じレシピとして扱われる
        var a = EditRecipe.Initial.WithFilter("basic", ParameterValues.Empty.With("brightness", 30));
        var b = EditRecipe.Initial.WithFilter("basic", ParameterValues.Empty.With("brightness", 30.0));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void EditRecipe_IsNotAffectedByLaterChangesToPassedDictionary()
    {
        var filters = new Dictionary<string, ParameterValues> { ["basic"] = Basic(30) };
        var recipe = new EditRecipe(ImageOrientation.Identity, null, filters);
        var copy = new EditRecipe(ImageOrientation.Identity, null, new Dictionary<string, ParameterValues>(filters));

        filters["basic"] = Basic(99);
        filters["mono"] = ParameterValues.Empty;

        Assert.Equal(30, recipe.GetFilter("basic").Get<int>("brightness"));
        Assert.False(recipe.Filters.ContainsKey("mono"));
        Assert.Equal(copy, recipe);
    }

    [Fact]
    public void EditRecipe_WithFilterAndGetFilter()
    {
        var recipe = EditRecipe.Initial.WithFilter("basic", Basic(30));

        Assert.Equal(Basic(30), recipe.GetFilter("basic"));
        Assert.Equal(ParameterValues.Empty, recipe.GetFilter("mono")); // 無いフィルターは空（既定値として扱われる）
        Assert.Empty(EditRecipe.Initial.Filters);                      // 元のレシピは変わらない

        var changed = recipe.WithFilter("basic", Basic(-10));
        Assert.Equal(-10, changed.GetFilter("basic").Get<int>("brightness"));
        Assert.Equal(30, recipe.GetFilter("basic").Get<int>("brightness"));
    }

    [Fact]
    public void EditRecipe_InvalidFilters_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new EditRecipe(ImageOrientation.Identity, null, null!));
        Assert.Throws<ArgumentException>(() => new EditRecipe(ImageOrientation.Identity, null,
            new Dictionary<string, ParameterValues> { [""] = ParameterValues.Empty }));
        Assert.Throws<ArgumentException>(() => new EditRecipe(ImageOrientation.Identity, null,
            new Dictionary<string, ParameterValues> { ["basic"] = null! }));
        Assert.Throws<ArgumentException>(() => EditRecipe.Initial.WithFilter("", ParameterValues.Empty));
        Assert.Throws<ArgumentNullException>(() => EditRecipe.Initial.WithFilter("basic", null!));
    }

    [Fact]
    public void EditRecipe_ToString_ShowsFilterContents()
    {
        var recipe = EditRecipe.Initial.WithFilter("basic", Basic(30));

        Assert.Contains("basic={brightness=30, contrast=0}", recipe.ToString());
    }

    // ---------------------------------------------------------------- E24: ExportSettings

    private static ExportSettings CoverSettings(IReadOnlyDictionary<string, ParameterValues> options) => new(
        "jpeg", options, null, OutputSize.ExactSize(1024, 768), new RgbColor(255, 255, 255));

    [Fact]
    public void ExportSettings_SameContentInDifferentDictionaries_IsEqual()
    {
        var a = CoverSettings(new Dictionary<string, ParameterValues>
        {
            ["jpeg"] = Quality(85),
            ["png"] = ParameterValues.Empty.With("interlace", false),
        });
        var b = CoverSettings(new Dictionary<string, ParameterValues>
        {
            ["png"] = ParameterValues.Empty.With("interlace", false),
            ["jpeg"] = Quality(85),
        });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ExportSettings_DifferentContent_IsNotEqual()
    {
        var settings = CoverSettings(new Dictionary<string, ParameterValues> { ["jpeg"] = Quality(85) });

        Assert.NotEqual(settings, settings with { FormatId = "png" });
        Assert.NotEqual(settings, settings.WithOptions("jpeg", Quality(70)));
        Assert.NotEqual(settings, settings.WithOptions("png", ParameterValues.Empty));
        Assert.NotEqual(settings, settings with { SizeLimitBytes = 300 * 1024 });
        Assert.NotEqual(settings, settings with { Size = OutputSize.LongSideOf(1920) });
        Assert.NotEqual(settings, settings with { Size = OutputSize.ExactSize(1280, 960) });
        Assert.NotEqual(settings, settings with { Background = new RgbColor(0, 0, 0) });
    }

    [Fact]
    public void ExportSettings_OptionsAreCopiedAndKeptPerFormat()
    {
        var options = new Dictionary<string, ParameterValues> { ["jpeg"] = Quality(85) };
        var settings = CoverSettings(options);
        options["jpeg"] = Quality(10);

        // 渡した辞書を後で変えても影響しない
        Assert.Equal(85, settings.GetOptions("jpeg").Get<int>("quality"));

        // 形式を切り替えても、それぞれの形式のオプションは残っている
        var png = settings with { FormatId = "png" };
        var backToJpeg = png.WithOptions("png", ParameterValues.Empty.With("interlace", true)) with { FormatId = "jpeg" };
        Assert.Equal(85, backToJpeg.GetOptions("jpeg").Get<int>("quality"));
        Assert.True(backToJpeg.GetOptions("png").Get<bool>("interlace"));
        Assert.Equal(ParameterValues.Empty, settings.GetOptions("bmp"));
    }

    [Fact]
    public void ExportPreset_ComparesSettingsByContent()
    {
        var a = new ExportPreset("表紙画像", CoverSettings(new Dictionary<string, ParameterValues> { ["jpeg"] = Quality(85) }), true);
        var b = new ExportPreset("表紙画像", CoverSettings(new Dictionary<string, ParameterValues> { ["jpeg"] = Quality(85) }), true);

        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { IsBuiltIn = false });
        Assert.NotEqual(a, b with { Name = "表紙画像 2" });
    }

    // ---------------------------------------------------------------- CropAspect

    [Fact]
    public void CropAspect_Fixed_IsReducedAndComparedByValue()
    {
        Assert.Equal(CropAspect.Fixed(4, 3), CropAspect.Fixed(1024, 768));
        Assert.Equal(new AspectRatio(16, 9), CropAspect.Fixed(1920, 1080).Ratio);
        Assert.NotEqual(CropAspect.Fixed(4, 3), CropAspect.Fixed(3, 4));
        Assert.NotEqual(CropAspect.Free, CropAspect.Original);
    }

    [Fact]
    public void CropAspect_RatioIsIgnoredUnlessFixed()
    {
        var free = new CropAspect(CropAspectKind.Free, new AspectRatio(4, 3));
        var original = new CropAspect(CropAspectKind.Original, new AspectRatio(16, 9));

        Assert.Equal(default, free.Ratio);
        Assert.Equal(CropAspect.Free, free);
        Assert.Equal(CropAspect.Original, original);
    }

    [Fact]
    public void CropAspect_FixedWithoutRatio_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CropAspect(CropAspectKind.Fixed));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CropAspect((CropAspectKind)99));
    }

    [Fact]
    public void CropAspect_Swapped_SwapsOnlyFixed()
    {
        Assert.Equal(CropAspect.Fixed(3, 4), CropAspect.Fixed(4, 3).Swapped());
        Assert.Equal(CropAspect.Fixed(9, 16), CropAspect.Fixed(16, 9).Swapped());
        Assert.Equal(CropAspect.Fixed(1, 1), CropAspect.Fixed(1, 1).Swapped());
        Assert.Equal(CropAspect.Free, CropAspect.Free.Swapped());
        Assert.Equal(CropAspect.Original, CropAspect.Original.Swapped());
    }

    // ---------------------------------------------------------------- OutputSize

    [Fact]
    public void OutputSize_Factories()
    {
        var exact = OutputSize.ExactSize(1200, 630);
        Assert.Equal((OutputSizeMode.Exact, 1200, 630), (exact.Mode, exact.Width, exact.Height));
        Assert.Equal(new AspectRatio(40, 21), exact.ExactAspect);

        var longSide = OutputSize.LongSideOf(1920);
        Assert.Equal((OutputSizeMode.LongSide, 1920), (longSide.Mode, longSide.LongSide));
        Assert.Null(longSide.ExactAspect);

        Assert.Equal(OutputSizeMode.Original, OutputSize.OriginalSize.Mode);
        Assert.Null(OutputSize.OriginalSize.ExactAspect);
    }

    [Fact]
    public void OutputSize_KeepsValuesOfOtherModes()
    {
        // 方式を切り替えても、それぞれの値は残る
        var exact = OutputSize.ExactSize(1280, 960);
        var longSide = exact with { Mode = OutputSizeMode.LongSide, LongSide = 2400 };
        var backToExact = longSide with { Mode = OutputSizeMode.Exact };

        Assert.Equal((1280, 960), (backToExact.Width, backToExact.Height));
        Assert.Equal(2400, backToExact.LongSide);
    }

    [Theory]
    [InlineData(OutputSizeMode.Exact, 1024, 768, 0, true)]   // 使わない長辺は範囲外でもよい
    [InlineData(OutputSizeMode.Exact, 16, 8192, 1920, true)]
    [InlineData(OutputSizeMode.Exact, 15, 768, 1920, false)]
    [InlineData(OutputSizeMode.Exact, 1024, 8193, 1920, false)]
    [InlineData(OutputSizeMode.LongSide, 0, 0, 1920, true)]  // 使わない幅×高さは範囲外でもよい
    [InlineData(OutputSizeMode.LongSide, 1024, 768, 15, false)]
    [InlineData(OutputSizeMode.LongSide, 1024, 768, 8193, false)]
    [InlineData(OutputSizeMode.Original, 0, 0, 0, true)]
    public void OutputSize_IsValid_ChecksOnlyValuesOfMode(OutputSizeMode mode, int width, int height, int longSide, bool expected)
    {
        Assert.Equal(expected, new OutputSize(mode, width, height, longSide).IsValid);
    }
}
