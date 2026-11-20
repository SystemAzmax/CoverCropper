using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Mcp.Tools;
using WebDeza.Modules;

namespace WebDeza.Mcp.Tests;

public class CatalogToolsTests
{
    private static (CatalogTools Tools, ImagingCatalog Catalog) Create()
    {
        var catalog = new ImagingCatalog();
        return (new CatalogTools(catalog), catalog);
    }

    [Fact]
    public void Catalog_RegistersSameFormatsAndFiltersAsImagingModules()
    {
        // S01: MCP サーバーの形式とフィルターは、webDeza と同じ ImagingModules で登録する
        var formats = new FormatRegistry();
        var filters = new FilterRegistry();
        ImagingModules.Register(formats, filters);

        var catalog = new ImagingCatalog();

        Assert.Equal(formats.All.Select(f => f.Id), catalog.Formats.All.Select(f => f.Id));
        Assert.Equal(filters.All.Select(f => f.Id), catalog.Filters.All.Select(f => f.Id));
    }

    [Fact]
    public void ListFormats_DescribesEveryRegisteredFormatInOrder()
    {
        // S02
        var (tools, catalog) = Create();

        var formats = tools.ListFormats();

        Assert.Equal(catalog.Formats.All.Select(f => f.Id), formats.Select(f => f.Id));

        var jpeg = formats.Single(f => f.Id == "jpeg");
        Assert.Equal("JPEG", jpeg.Name);
        Assert.Equal(".jpg", jpeg.Extensions[0]);
        Assert.True(jpeg.CanOpen);
        Assert.True(jpeg.CanSave);
        Assert.False(jpeg.KeepsTransparency);
        Assert.Equal("quality", jpeg.SizeTuningOption);
        var quality = Assert.Single(jpeg.Options);
        Assert.Equal("quality", quality.Key);
        Assert.Equal("integer", quality.Kind);
        Assert.Equal(85L, quality.Default);
        Assert.Equal(1, quality.Min);
        Assert.Equal(100, quality.Max);
        Assert.Null(quality.Choices);

        var png = formats.Single(f => f.Id == "png");
        Assert.True(png.KeepsTransparency);
        Assert.Null(png.SizeTuningOption);
        var interlace = Assert.Single(png.Options);
        Assert.Equal("boolean", interlace.Kind);
        Assert.Equal(false, interlace.Default);
        Assert.Null(interlace.Min);
        Assert.Null(interlace.Max);

        var tiff = formats.Single(f => f.Id == "tiff");
        var compression = Assert.Single(tiff.Options);
        Assert.Equal("choice", compression.Kind);
        Assert.NotNull(compression.Choices);
        Assert.Equal(new[] { "none", "lzw", "zip" }, compression.Choices!.Select(c => c.Value));

        // 読めるだけの形式は、保存できず、オプションも無い
        var gif = formats.Single(f => f.Id == "gif");
        Assert.True(gif.CanOpen);
        Assert.False(gif.CanSave);
        Assert.Null(gif.SizeTuningOption);
        Assert.Empty(gif.Options);

        // OS の拡張機能が要る形式は、その説明を返す
        Assert.NotNull(formats.Single(f => f.Id == "heif").Requirement);
    }

    [Fact]
    public void ListFilters_DescribesEveryRegisteredFilterInOrder()
    {
        // S03
        var (tools, catalog) = Create();

        var filters = tools.ListFilters();

        Assert.Equal(catalog.Filters.All.Select(f => f.Id), filters.Select(f => f.Id));

        var blur = filters.Single(f => f.Id == BlurFilter.FilterId);
        Assert.Equal("ぼかし", blur.Name);
        var radius = Assert.Single(blur.Parameters);
        Assert.Equal("number", radius.Kind);
        Assert.Equal(0.0, radius.Default);
        Assert.Equal(0, radius.Min);
        Assert.Equal(100, radius.Max);
        Assert.Equal("px", radius.Unit);
    }
}
