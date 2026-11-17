using System.IO;
using System.Text.Json.Nodes;
using WebDeza.Composition;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Models;
using WebDeza.Services;

namespace WebDeza.Tests.Services;

public class SettingsServiceTests
{
    private readonly FormatRegistry _formats = CreateFormats();

    private static FormatRegistry CreateFormats()
    {
        var formats = new FormatRegistry();
        AppModules.Register(formats, new FilterRegistry(), new WebDeza.Views.Canvas.ToolRegistry());
        return formats;
    }

    /// <summary>JSON を書いた設定ファイルを読む</summary>
    private SettingsLoadResult LoadJson(TempFolder temp, string json)
    {
        var path = temp.Combine("settings.json");
        File.WriteAllText(path, json);
        return new SettingsService(_formats, path).Load();
    }

    private static ParameterValues Quality(int quality) => ParameterValues.Empty.With("quality", quality);

    // ---------------------------------------------------------------- 既定値・M20

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsWithoutMessages()
    {
        using var temp = new TempFolder();

        var result = new SettingsService(_formats, temp.Combine("none.json")).Load();

        Assert.Empty(result.Messages);
        var export = result.Settings.Export.ToExportSettings();
        // 初回は JPEG・品質 85・背景は白（大きさは指定しない）
        Assert.Equal("jpeg", export.FormatId);
        Assert.Equal(85, export.GetOptions("jpeg").Get<int>("quality"));
        Assert.Null(export.SizeLimitBytes);
        Assert.Equal(new RgbColor(255, 255, 255), export.Background);
        Assert.Equal(SelectionAspectData.FreeKind, result.Settings.SelectionAspect.Kind);
        Assert.Null(result.Settings.SelectionAspect.ToAspectRatio(600, 400));
        Assert.Null(result.Settings.OutputFolder);
        Assert.Null(result.Settings.WindowWidth);
        // 書き出せる形式はすべて、オプションの既定値を持つ
        Assert.Equal(new[] { "bmp", "jpeg", "png", "tiff" }, result.Settings.Export.FormatOptions.Keys.Order());
        Assert.Equal(ExportSettingsData.DefaultSizeLimitKB, result.Settings.Export.SizeLimitKB);
    }

    [Theory]
    [InlineData("{ \"OutputFolder\": ")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"Export\": { \"FormatOptions\": { \"jpeg\": [1, 2] } } }")] // オプションがオブジェクトでない
    public void Load_CorruptedJson_ReturnsDefaultsWithM20(string json)
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, json);

        Assert.Equal(new[] { Messages.M20_SettingsLoadFailed }, result.Messages);
        Assert.Equal("jpeg", result.Settings.Export.FormatId);
        Assert.Equal(json, File.ReadAllText(temp.Combine("settings.json"))); // 読めなかったファイルは変えない
    }

    // ---------------------------------------------------------------- V16: 値の検証

    [Theory]
    [InlineData("webp")]       // 登録はあるが書き出せない形式
    [InlineData("avif-plugin")] // 登録されていない形式（プラグインを外した場合など）
    [InlineData("")]
    public void Load_UnknownFormatId_BecomesJpegWithM25(string formatId)
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, $$"""{ "Export": { "FormatId": "{{formatId}}" } }""");

        Assert.Equal("jpeg", result.Settings.Export.FormatId);
        Assert.Equal(new[] { Messages.M25_FormatNotFound(formatId) }, result.Messages);
    }


    [Theory]
    [InlineData("""{ "quality": 500 }""")]     // 範囲外
    [InlineData("""{ "quality": 0 }""")]
    [InlineData("""{ "quality": 72.5 }""")]    // 整数でない
    [InlineData("""{ "quality": "high" }""")]  // 種類が合わない
    [InlineData("""{ "quality": true }""")]
    [InlineData("""{}""")]                     // 無い
    public void Load_InvalidJpegQuality_BecomesDefault(string jpegOptions)
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, $$"""{ "Export": { "FormatOptions": { "jpeg": {{jpegOptions}} } } }""");

        Assert.Equal(Quality(85), result.Settings.Export.FormatOptions["jpeg"]);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Load_ValidOptions_AreKept_AndOnlyInvalidItemsBecomeDefault()
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, """
            {
              "Export": {
                "FormatId": "tiff",
                "FormatOptions": {
                  "jpeg": { "quality": 72, "unknown": 1 },
                  "png": { "interlace": true },
                  "tiff": { "compression": "jpeg" },
                  "webp-writer": { "quality": 80, "lossless": false }
                }
              }
            }
            """);

        var options = result.Settings.Export.FormatOptions;
        Assert.Equal("tiff", result.Settings.Export.FormatId);
        Assert.Equal(Quality(72), options["jpeg"]);  // 記述に無いキーは捨てる
        Assert.Equal(ParameterValues.Empty.With("interlace", true), options["png"]);
        Assert.Equal(ParameterValues.Empty.With("compression", "lzw"), options["tiff"]); // 選択肢に無い値は既定値
        Assert.Equal(ParameterValues.Empty, options["bmp"]);
        // 登録されていない形式のオプションは、プラグインを戻したときのために残す
        Assert.Equal(ParameterValues.Empty.With("quality", 80).With("lossless", false), options["webp-writer"]);
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(-5, 300)]
    [InlineData(1_000_001, 300)]
    [InlineData(1, 1)]
    [InlineData(1_000_000, 1_000_000)]
    [InlineData(250, 250)]
    public void Load_SizeLimitOutOfRange_BecomesDefault(long stored, long expected)
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, $$"""{ "Export": { "UseSizeLimit": true, "SizeLimitKB": {{stored}} } }""");

        Assert.Equal(expected, result.Settings.Export.SizeLimitKB);
        Assert.Equal(expected * 1024, result.Settings.Export.ToExportSettings().SizeLimitBytes);
    }

    [Theory]
    [InlineData("#336699", "#336699")]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("336699", "#FFFFFF")]
    [InlineData("#12345", "#FFFFFF")]
    [InlineData("#GGGGGG", "#FFFFFF")]
    public void Load_InvalidBackground_BecomesWhite(string stored, string expected)
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, $$"""{ "Export": { "Background": "{{stored}}" } }""");

        Assert.Equal(expected, result.Settings.Export.Background);
    }

    [Theory]
    [InlineData("""{ "Kind": "fixed", "Width": 16, "Height": 9 }""", "fixed", 16, 9)]
    [InlineData("""{ "Kind": "fixed", "Width": 1200, "Height": 630 }""", "fixed", 1200, 630)] // カスタム（約分はしない。縦横の入れ替えで元に戻せるように）
    [InlineData("""{ "Kind": "fixed", "Width": 10000, "Height": 1 }""", "fixed", 10000, 1)]
    [InlineData("""{ "Kind": "original", "Width": 5, "Height": 5 }""", "original", 0, 0)]
    [InlineData("""{ "Kind": "free" }""", "free", 0, 0)]
    [InlineData("""{ "Kind": "fixed", "Width": 0, "Height": 9 }""", "free", 0, 0)]   // 範囲外は自由
    [InlineData("""{ "Kind": "fixed", "Width": 10001, "Height": 9 }""", "free", 0, 0)]
    [InlineData("""{ "Kind": "diagonal" }""", "free", 0, 0)]
    [InlineData("""null""", "free", 0, 0)]
    public void Load_SelectionAspect_IsValidated(string aspect, string kind, int width, int height)
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, $$"""{ "SelectionAspect": {{aspect}} }""");

        var actual = result.Settings.SelectionAspect;
        Assert.Equal((kind, width, height), (actual.Kind, actual.Width, actual.Height));
        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Load_WindowValuesThatCannotBeUsed_BecomeNull()
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, """{ "WindowLeft": 10, "WindowTop": -20, "WindowWidth": 0, "WindowHeight": -100, "WindowMaximized": true }""");

        var s = result.Settings;
        Assert.Equal((10.0, -20.0), (s.WindowLeft, s.WindowTop));
        Assert.Null(s.WindowWidth);
        Assert.Null(s.WindowHeight);
        Assert.True(s.WindowMaximized);
    }

    [Fact]
    public void Load_ExportDialogSize_IsKeptOrDropped()
    {
        using var temp = new TempFolder();

        var kept = LoadJson(temp, """{ "ExportDialogWidth": 1200, "ExportDialogHeight": 800 }""").Settings;
        Assert.Equal((1200.0, 800.0), (kept.ExportDialogWidth, kept.ExportDialogHeight));

        var dropped = LoadJson(temp, """{ "ExportDialogWidth": 0, "ExportDialogHeight": -5 }""").Settings;
        Assert.Null(dropped.ExportDialogWidth);
        Assert.Null(dropped.ExportDialogHeight);
    }

    // ---------------------------------------------------------------- V18: 保存して読み直す

    [Fact]
    public void SaveThenLoad_ExportSettingsAndSelectionAspect_RoundTrip()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("sub", "settings.json");
        var service = new SettingsService(_formats, path);

        var export = new ExportSettings(
            "png",
            new Dictionary<string, ParameterValues>
            {
                ["jpeg"] = Quality(72),
                ["png"] = ParameterValues.Empty.With("interlace", true),
                ["bmp"] = ParameterValues.Empty,
                ["tiff"] = ParameterValues.Empty.With("compression", "zip"),
            },
            SizeLimitBytes: null,
            new RgbColor(0x33, 0x66, 0x99));

        service.Save(new AppSettings
        {
            OutputFolder = @"D:\web\表紙",
            LastOpenFolder = @"C:\写真",
            Export = ExportSettingsData.From(export, rememberedSizeLimitKB: 512),
            SelectionAspect = SelectionAspectData.Fixed(9, 16),
            WindowLeft = 100, WindowTop = 50, WindowWidth = 1500, WindowHeight = 950, WindowMaximized = true,
        });
        var result = new SettingsService(_formats, path).Load();

        Assert.Empty(result.Messages);
        var s = result.Settings;
        Assert.Equal(export, s.Export.ToExportSettings());
        Assert.Equal(512, s.Export.SizeLimitKB); // 「品質を指定」のときも上限の値を覚えている
        Assert.Equal((SelectionAspectData.FixedKind, 9, 16), (s.SelectionAspect.Kind, s.SelectionAspect.Width, s.SelectionAspect.Height));
        Assert.Equal((@"D:\web\表紙", @"C:\写真"), (s.OutputFolder, s.LastOpenFolder));
        Assert.Equal((100.0, 50.0, 1500.0, 950.0, true), (s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight, s.WindowMaximized));
    }

    [Fact]
    public void Save_WritesOptionsAsPlainJsonObject()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("settings.json");
        var service = new SettingsService(_formats, path);
        var settings = service.Load().Settings;

        service.Save(settings);

        // パラメーターの値は「キー → 数値・真偽・文字列」の JSON オブジェクト（設計書 F-12）
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        var options = json["Export"]!["FormatOptions"]!;
        Assert.Equal(85, options["jpeg"]!["quality"]!.GetValue<long>());
        Assert.False(options["png"]!["interlace"]!.GetValue<bool>());
        Assert.Equal("lzw", options["tiff"]!["compression"]!.GetValue<string>());
        Assert.Null(json["Export"]!["Size"]); // 書き出しの大きさは持たない
        Assert.Equal("#FFFFFF", json["Export"]!["Background"]!.GetValue<string>());
        Assert.Equal("free", json["SelectionAspect"]!["Kind"]!.GetValue<string>());
        Assert.Null(json["SelectionAspect"]!["IsValid"]); // 計算で求める値は書かない
    }

    [Fact]
    public void CreateDefaultExport_IsJpegQuality85()
    {
        var export = new SettingsService(_formats, "unused.json").CreateDefaultExport().ToExportSettings();

        Assert.Equal("jpeg", export.FormatId);
        Assert.Equal(Quality(85), export.GetOptions("jpeg"));
        Assert.Null(export.SizeLimitBytes);
        Assert.Equal(new RgbColor(255, 255, 255), export.Background);
    }

    [Fact]
    public void DefaultConstructor_UsesWebDezaDataFolder()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "webDeza", "settings.json");

        Assert.Equal(expected, new SettingsService(_formats).FilePath);
    }
}
