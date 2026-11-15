using System.Reflection;
using WebDeza.Composition;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;

namespace WebDeza.Tests;

public class AppModulesTests
{
    [Fact]
    public void Register_AddsStandardFormatsAndBasicAdjustFilter()
    {
        var formats = new FormatRegistry();
        var filters = new FilterRegistry();

        AppModules.Register(formats, filters);

        // 設計書 5.4 の 10 形式。書き出せるのは JPEG / PNG / BMP / TIFF で、先頭の JPEG が既定
        Assert.Equal(new[] { "jpeg", "png", "bmp", "tiff", "gif", "jxr", "ico", "webp", "heif", "avif" },
            formats.All.Select(f => f.Id));
        Assert.Equal(new[] { "jpeg", "png", "bmp", "tiff" }, formats.Encodable.Select(f => f.Id));
        // フィルターは基本補正だけ
        var filter = Assert.Single(filters.All);
        Assert.Equal(BasicAdjustFilter.FilterId, filter.Id);
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
        Assert.Equal("書き出しました: a.jpg（182.4 KB）", Messages.M04_Exported("a.jpg", "182.4 KB"));
        Assert.Equal("3 件を一覧に追加しました。", Messages.M09_Added(3, 0, 0));
        Assert.Equal("3 件を一覧に追加しました。重複 1 件・非対応 2 件を除きました。", Messages.M09_Added(3, 1, 2));
        Assert.Equal("この形式を読むには Microsoft Store の「HEIF 画像拡張機能」 が必要です。",
            Messages.M21_Requirement("Microsoft Store の「HEIF 画像拡張機能」"));
        Assert.Equal("ここに画像やフォルダをドラッグ＆ドロップ、または［開く］", Messages.Placeholder);
    }
}
