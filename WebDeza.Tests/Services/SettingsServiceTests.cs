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
        AppModules.Register(formats, new FilterRegistry());
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
    public void Load_MissingFile_ReturnsCoverImageDefaultsWithoutMessages()
    {
        using var temp = new TempFolder();

        var result = new SettingsService(_formats, temp.Combine("none.json")).Load();

        Assert.Empty(result.Messages);
        var export = result.Settings.Export.ToExportSettings();
        // 初回は組み込みのプリセット「表紙画像」と同じ: JPEG・品質 85・1024×768・背景は白
        Assert.Equal("jpeg", export.FormatId);
        Assert.Equal(85, export.GetOptions("jpeg").Get<int>("quality"));
        Assert.Null(export.SizeLimitBytes);
        Assert.Equal(OutputSize.ExactSize(1024, 768), export.Size);
        Assert.Equal(new RgbColor(255, 255, 255), export.Background);
        Assert.Equal(CropAspect.Free, result.Settings.CropAspect.ToCropAspect());
        Assert.Empty(result.Settings.UserPresets);
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

    [Fact]
    public void Load_UnknownFormatIdInSeveralPlaces_ReportsM25OncePerId()
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, """
            {
              "Export": { "FormatId": "heic-writer" },
              "UserPresets": [
                { "Name": "A", "Export": { "FormatId": "heic-writer" } },
                { "Name": "B", "Export": { "FormatId": "png" } },
                { "Name": "C", "Export": { "FormatId": "jxl" } }
              ]
            }
            """);

        Assert.Equal(new[] { Messages.M25_FormatNotFound("heic-writer"), Messages.M25_FormatNotFound("jxl") }, result.Messages);
        Assert.Equal(new[] { "jpeg", "png", "jpeg" }, result.Settings.UserPresets.Select(p => p.Export.FormatId));
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
    [InlineData("""{ "Mode": "exact", "Width": 15, "Height": 768, "LongSide": 1920 }""", "exact", 1024, 768, 1920)]
    [InlineData("""{ "Mode": "exact", "Width": 1200, "Height": 9000, "LongSide": 1920 }""", "exact", 1024, 768, 1920)]
    [InlineData("""{ "Mode": "exact", "Width": 1200, "Height": 630, "LongSide": 0 }""", "exact", 1200, 630, 1920)]
    [InlineData("""{ "Mode": "longSide", "Width": 16, "Height": 8192, "LongSide": 8193 }""", "longSide", 16, 8192, 1920)]
    [InlineData("""{ "Mode": "original", "Width": 1080, "Height": 1080, "LongSide": 2400 }""", "original", 1080, 1080, 2400)]
    [InlineData("""{ "Mode": "huge", "Width": 1080, "Height": 1080, "LongSide": 2400 }""", "exact", 1080, 1080, 2400)]
    [InlineData("""null""", "exact", 1024, 768, 1920)]
    public void Load_SizeOutOfRange_BecomesDefault(string size, string mode, int width, int height, int longSide)
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, $$"""{ "Export": { "Size": {{size}} } }""");

        var actual = result.Settings.Export.Size;
        Assert.Equal((mode, width, height, longSide), (actual.Mode, actual.Width, actual.Height, actual.LongSide));
        Assert.True(actual.ToOutputSize().IsValid);
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
    [InlineData("""{ "Kind": "fixed", "Width": 8, "Height": 6 }""", "fixed", 4, 3)] // 約分して持つ
    [InlineData("""{ "Kind": "original" }""", "original", 0, 0)]
    [InlineData("""{ "Kind": "fixed", "Width": 0, "Height": 9 }""", "free", 0, 0)]
    [InlineData("""{ "Kind": "diagonal" }""", "free", 0, 0)]
    public void Load_CropAspect_IsValidated(string aspect, string kind, int width, int height)
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, $$"""{ "CropAspect": {{aspect}} }""");

        var actual = result.Settings.CropAspect;
        Assert.Equal((kind, width, height), (actual.Kind, actual.Width, actual.Height));
    }

    [Fact]
    public void Load_UserPresetsWithoutNameOrDuplicated_AreDropped()
    {
        using var temp = new TempFolder();

        var result = LoadJson(temp, """
            {
              "UserPresets": [
                { "Name": "  ブログ用  ", "Export": { "FormatId": "png" } },
                { "Name": "", "Export": {} },
                { "Name": "ブログ用", "Export": { "FormatId": "bmp" } },
                null,
                { "Name": "サムネイル", "Export": null }
              ]
            }
            """);

        Assert.Equal(new[] { "ブログ用", "サムネイル" }, result.Settings.UserPresets.Select(p => p.Name));
        Assert.Equal("png", result.Settings.UserPresets[0].Export.FormatId);
        Assert.Equal("jpeg", result.Settings.UserPresets[1].Export.FormatId);
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

    // ---------------------------------------------------------------- V18: 保存して読み直す

    [Fact]
    public void SaveThenLoad_ExportSettingsCropAspectAndUserPresets_RoundTrip()
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
            OutputSize.LongSideOf(2400),
            new RgbColor(0x33, 0x66, 0x99));
        var preset = new ExportPreset("Web 300KB", new ExportSettings(
            "jpeg",
            new Dictionary<string, ParameterValues>
            {
                ["jpeg"] = Quality(90),
                ["png"] = ParameterValues.Empty.With("interlace", false),
                ["bmp"] = ParameterValues.Empty,
                ["tiff"] = ParameterValues.Empty.With("compression", "lzw"),
            },
            SizeLimitBytes: 300 * 1024,
            OutputSize.ExactSize(1200, 630),
            new RgbColor(0, 0, 0)), IsBuiltIn: false);

        service.Save(new AppSettings
        {
            OutputFolder = @"D:\web\表紙",
            LastOpenFolder = @"C:\写真",
            Export = ExportSettingsData.From(export, rememberedSizeLimitKB: 512),
            CropAspect = CropAspectData.From(CropAspect.Fixed(16, 9)),
            UserPresets = { PresetData.From(preset) },
            WindowLeft = 100, WindowTop = 50, WindowWidth = 1500, WindowHeight = 950, WindowMaximized = true,
        });
        var result = new SettingsService(_formats, path).Load();

        Assert.Empty(result.Messages);
        var s = result.Settings;
        Assert.Equal(export, s.Export.ToExportSettings());
        Assert.Equal(512, s.Export.SizeLimitKB); // 「品質を指定」のときも上限の値を覚えている
        Assert.Equal(CropAspect.Fixed(16, 9), s.CropAspect.ToCropAspect());
        Assert.Equal(new[] { preset }, s.UserPresets.Select(p => p.ToPreset()));
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
        Assert.Equal("exact", json["Export"]!["Size"]!["Mode"]!.GetValue<string>());
        Assert.Equal("#FFFFFF", json["Export"]!["Background"]!.GetValue<string>());
        Assert.Equal("free", json["CropAspect"]!["Kind"]!.GetValue<string>());
    }

    [Fact]
    public void CreateDefaultExport_IsCoverImageSettings()
    {
        var export = new SettingsService(_formats, "unused.json").CreateDefaultExport().ToExportSettings();

        Assert.Equal("jpeg", export.FormatId);
        Assert.Equal(Quality(85), export.GetOptions("jpeg"));
        Assert.Equal(OutputSize.ExactSize(1024, 768), export.Size);
        Assert.Null(export.SizeLimitBytes);
    }

    [Fact]
    public void DefaultConstructor_UsesWebDezaDataFolder()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "webDeza", "settings.json");

        Assert.Equal(expected, new SettingsService(_formats).FilePath);
    }
}
