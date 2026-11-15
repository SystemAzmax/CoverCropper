using System.IO;
using WebDeza.Core;

namespace WebDeza.Tests.Core;

public class AppPathsTests
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    [Fact]
    public void GetDataFolder_IsAppDataPlusAppName()
    {
        Assert.Equal(Path.Combine(AppData, "webDeza"), AppPaths.GetDataFolder("webDeza"));
        Assert.Equal(Path.Combine(AppData, "CoverCropper"), AppPaths.GetDataFolder("CoverCropper"));
    }

    [Fact]
    public void GetLogFileAndSettingsFile_AreUnderDataFolder()
    {
        Assert.Equal(Path.Combine(AppData, "webDeza", "logs", "app.log"), AppPaths.GetLogFile("webDeza"));
        Assert.Equal(Path.Combine(AppData, "webDeza", "settings.json"), AppPaths.GetSettingsFile("webDeza"));
    }

    [Fact]
    public void DifferentApps_UseDifferentFolders()
    {
        // webDeza と CoverCropper は設定もログも別（同時に動かせる）
        Assert.NotEqual(AppPaths.GetDataFolder("CoverCropper"), AppPaths.GetDataFolder(AppInfo.ProductName));
        Assert.NotEqual(AppPaths.GetLogFile("CoverCropper"), AppPaths.GetLogFile(AppInfo.ProductName));
        Assert.NotEqual(AppPaths.GetSettingsFile("CoverCropper"), AppPaths.GetSettingsFile(AppInfo.ProductName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyAppName_Throws(string appName)
    {
        Assert.Throws<ArgumentException>(() => AppPaths.GetDataFolder(appName));
        Assert.Throws<ArgumentException>(() => AppPaths.GetLogFile(appName));
        Assert.Throws<ArgumentException>(() => AppPaths.GetSettingsFile(appName));
    }
}
