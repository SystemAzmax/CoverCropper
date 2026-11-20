using System.Reflection;
using WebDeza.Composition;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Modules;
using WebDeza.Views.Canvas;

namespace WebDeza.Tests;

public class AppModulesTests
{
    [Fact]
    public void Register_AddsStandardFormatsFiltersAndTools()
    {
        var formats = new FormatRegistry();
        var filters = new FilterRegistry();
        var tools = new ToolRegistry();

        AppModules.Register(formats, filters, tools);

        // 設計書 5.4 の 10 形式。書き出せるのは JPEG / PNG / BMP / TIFF で、先頭の JPEG が既定
        Assert.Equal(new[] { "jpeg", "png", "bmp", "tiff", "gif", "jxr", "ico", "webp", "heif", "avif" },
            formats.All.Select(f => f.Id));
        Assert.Equal(new[] { "jpeg", "png", "bmp", "tiff" }, formats.Encodable.Select(f => f.Id));
        // フィルターは ぼかし・基本補正 の順（設計書 F-06）
        Assert.Equal(new[] { BlurFilter.FilterId, BasicAdjustFilter.FilterId }, filters.All.Select(f => f.Id));
        // 道具は 選択（既定）・手のひら の順（設計書 F-03）
        Assert.Equal(new[] { SelectToolFactory.ToolId, HandToolFactory.ToolId }, tools.All.Select(t => t.Id));
    }

    [Fact]
    public void Register_UsesImagingModulesForFormatsAndFilters()
    {
        // 形式とフィルターは、MCP サーバーと共用の ImagingModules で登録する（設計書 1.3・5.12）
        var formats = new FormatRegistry();
        var filters = new FilterRegistry();
        AppModules.Register(formats, filters, new ToolRegistry());

        var imagingFormats = new FormatRegistry();
        var imagingFilters = new FilterRegistry();
        ImagingModules.Register(imagingFormats, imagingFilters);

        Assert.Equal(imagingFormats.All.Select(f => f.Id), formats.All.Select(f => f.Id));
        Assert.Equal(imagingFilters.All.Select(f => f.Id), filters.All.Select(f => f.Id));
    }

    [Fact]
    public void AppInfo_ProductNameAndAssemblyName_AreWebDezaInLowerCamel()
    {
        // 設計書 5.9: 製品名と exe 名は webDeza、名前空間は WebDeza
        Assert.Equal("webDeza", AppInfo.ProductName);
        Assembly assembly = typeof(AppInfo).Assembly;
        Assert.Equal("webDeza", assembly.GetName().Name);
        Assert.Equal("WebDeza", typeof(AppInfo).Namespace);
    }

    [Fact]
    public void Messages_FixedTextsMatchDesign()
    {
        Assert.Equal("対応していないファイル形式です。", Messages.M01_UnsupportedFormat);
        Assert.Equal("画像を読み込めませんでした。（壊れています）", Messages.M02_LoadFailed("壊れています"));
        Assert.Equal("保存しました: a.jpg（182.4 KB）", Messages.M04_Exported("a.jpg", "182.4 KB"));
        Assert.Equal("3 件を開きました。", Messages.M09_Opened(3, 0));
        Assert.Equal("3 件を開きました。非対応 2 件を除きました。", Messages.M09_Opened(3, 2));
        Assert.Equal("同時に開けるのは 50 枚までです。2 件は開きませんでした。", Messages.M15_OverLimit(50, 2));
        Assert.Equal("「a.jpg」には保存していない編集があります。閉じますか？", Messages.M16_ConfirmClose("a.jpg"));
        Assert.Equal("編集できませんでした。（メモリが不足しています）", Messages.M28_EditFailed(Messages.OutOfMemory));
        Assert.Equal("2 件を開けませんでした。\na.jpg: 壊れています\nb.png: 対応していないファイル形式です。",
            Messages.M29_OpenFailed(new[] { ("a.jpg", "壊れています"), ("b.png", Messages.M01_UnsupportedFormat) }));
        Assert.Equal("この形式を読むには Microsoft Store の「HEIF 画像拡張機能」 が必要です。",
            Messages.M21_Requirement("Microsoft Store の「HEIF 画像拡張機能」"));
        Assert.Equal("ここに画像やフォルダをドラッグ＆ドロップ、または［開く］", Messages.Placeholder);
    }
}
