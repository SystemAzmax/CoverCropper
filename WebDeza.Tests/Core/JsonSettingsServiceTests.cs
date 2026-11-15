using System.IO;
using WebDeza.Core;

namespace WebDeza.Tests.Core;

public class JsonSettingsServiceTests
{
    public sealed class SampleSettings
    {
        public string? OutputFolder { get; set; }
        public double? WindowWidth { get; set; }
        public bool Maximized { get; set; }
        public int Quality { get; set; } = 85;
        public List<string> Recent { get; set; } = new();
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsWithoutError()
    {
        using var temp = new TempFolder();
        var service = new JsonSettingsService<SampleSettings>(temp.Combine("none.json"));

        var settings = service.Load();

        Assert.Null(settings.OutputFolder);
        Assert.Equal(85, settings.Quality);
        Assert.Null(service.LastLoadError);
    }

    [Theory]
    [InlineData("{ \"OutputFolder\": ")]        // 途中で切れている
    [InlineData("これは JSON ではありません")]
    [InlineData("{ \"Quality\": \"高い\" }")]   // 型が合わない
    public void Load_CorruptedJson_ReturnsDefaultsAndSetsError(string json)
    {
        using var temp = new TempFolder();
        var path = temp.Combine("settings.json");
        File.WriteAllText(path, json);
        var service = new JsonSettingsService<SampleSettings>(path);

        var settings = service.Load();

        Assert.Equal(85, settings.Quality);
        Assert.NotNull(service.LastLoadError);
        Assert.Equal(json, File.ReadAllText(path)); // 読めなかったファイルは変えない
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    public void Load_NullOrEmptyObject_ReturnsDefaultsWithoutError(string json)
    {
        using var temp = new TempFolder();
        var path = temp.Combine("settings.json");
        File.WriteAllText(path, json);
        var service = new JsonSettingsService<SampleSettings>(path);

        var settings = service.Load();

        Assert.Equal(85, settings.Quality);
        Assert.Null(service.LastLoadError);
    }

    [Fact]
    public void Load_AfterError_ClearsErrorWhenFileBecomesReadable()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("settings.json");
        File.WriteAllText(path, "{");
        var service = new JsonSettingsService<SampleSettings>(path);
        service.Load();
        Assert.NotNull(service.LastLoadError);

        service.Save(new SampleSettings { Quality = 70 });

        Assert.Equal(70, service.Load().Quality);
        Assert.Null(service.LastLoadError);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_AndCreatesFolder()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("sub", "deeper", "settings.json");
        var service = new JsonSettingsService<SampleSettings>(path);

        service.Save(new SampleSettings
        {
            OutputFolder = @"D:\web\表紙",
            WindowWidth = 1300.5,
            Maximized = true,
            Quality = 72,
            Recent = { "a.jpg", "b.png" },
        });
        var loaded = new JsonSettingsService<SampleSettings>(path).Load();

        Assert.Equal(@"D:\web\表紙", loaded.OutputFolder);
        Assert.Equal(1300.5, loaded.WindowWidth);
        Assert.True(loaded.Maximized);
        Assert.Equal(72, loaded.Quality);
        Assert.Equal(new[] { "a.jpg", "b.png" }, loaded.Recent);
        Assert.Equal(path, service.FilePath);
    }

    [Fact]
    public void Save_WritesIndentedJson_OverwritesAndLeavesNoTempFile()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("settings.json");
        var service = new JsonSettingsService<SampleSettings>(path);

        service.Save(new SampleSettings { Quality = 10 });
        service.Save(new SampleSettings { Quality = 20 });

        var json = File.ReadAllText(path);
        Assert.Contains("\"Quality\": 20", json);
        Assert.Contains(Environment.NewLine, json); // インデントつき（人が読める）
        Assert.Equal(new[] { path }, Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        using var temp = new TempFolder();
        Assert.Throws<ArgumentException>(() => new JsonSettingsService<SampleSettings>(""));
        Assert.Throws<ArgumentNullException>(
            () => new JsonSettingsService<SampleSettings>(temp.Combine("a.json")).Save(null!));
    }
}
