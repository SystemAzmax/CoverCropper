using System.IO;
using CoverCropper.Models;
using CoverCropper.Services;

namespace CoverCropper.Tests;

public class SettingsServiceTests
{
    [Fact] // U14
    public void Load_CorruptedJson_ReturnsDefaultsWithoutThrowing()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("settings.json");
        File.WriteAllText(path, "{ \"OutputFolder\": ");
        var service = new SettingsService(path);

        var settings = service.Load();

        Assert.Null(settings.OutputFolder);
        Assert.NotNull(service.LastLoadError);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var temp = new TempFolder();
        var service = new SettingsService(temp.Combine("none.json"));

        var settings = service.Load();

        Assert.Null(settings.OutputFolder);
        Assert.Null(service.LastLoadError);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        using var temp = new TempFolder();
        var path = temp.Combine(Path.Combine("sub", "settings.json"));
        var service = new SettingsService(path);

        service.Save(new AppSettings { OutputFolder = @"D:\web\cover", WindowWidth = 1300, WindowMaximized = true });
        var loaded = service.Load();

        Assert.Equal(@"D:\web\cover", loaded.OutputFolder);
        Assert.Equal(1300, loaded.WindowWidth);
        Assert.True(loaded.WindowMaximized);
        Assert.Contains("\"OutputFolder\"", File.ReadAllText(path));
    }
}
