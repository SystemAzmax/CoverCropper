using System.IO;
using CoverCropper.Services;

namespace CoverCropper.Tests;

public class OutputFileNamerTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 15, 30, 12);

    [Fact] // U11
    public void CreatePath_UsesBaseNameAndTimestamp()
    {
        var namer = new OutputFileNamer(_ => false);

        var path = namer.CreatePath(@"D:\out", @"C:\a\photo.png", Now);

        Assert.Equal(@"D:\out\photo_20261013_153012.jpg", path);
    }

    [Fact] // U12
    public void CreatePath_AppendsSequenceWhenExists()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\out\photo_20261013_153012.jpg",
        };
        var namer = new OutputFileNamer(existing.Contains);

        Assert.Equal(@"D:\out\photo_20261013_153012_2.jpg", namer.CreatePath(@"D:\out", @"C:\a\photo.png", Now));

        existing.Add(@"D:\out\photo_20261013_153012_2.jpg");
        Assert.Equal(@"D:\out\photo_20261013_153012_3.jpg", namer.CreatePath(@"D:\out", @"C:\a\photo.png", Now));
    }

    [Fact]
    public void CreatePath_WithRealFileSystem_DoesNotReturnExistingFile()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.Combine("photo_20261013_153012.jpg"), "x");
        var namer = new OutputFileNamer();

        var path = namer.CreatePath(temp.Path, @"C:\a\photo.jpg", Now);

        Assert.Equal(temp.Combine("photo_20261013_153012_2.jpg"), path);
    }

    [Fact]
    public void CreatePath_UsesInvariantDigitsRegardlessOfCulture()
    {
        var namer = new OutputFileNamer(_ => false);
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.EndsWith("photo_20261013_153012.jpg", namer.CreatePath(@"D:\out", @"C:\a\photo.png", Now));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
