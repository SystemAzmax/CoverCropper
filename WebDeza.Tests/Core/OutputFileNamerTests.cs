using System.IO;
using WebDeza.Core;

namespace WebDeza.Tests.Core;

public class OutputFileNamerTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 15, 30, 12);

    [Theory]
    [InlineData(".jpg", @"D:\out\photo_20261013_153012.jpg")]
    [InlineData(".png", @"D:\out\photo_20261013_153012.png")]
    [InlineData(".tif", @"D:\out\photo_20261013_153012.tif")]
    [InlineData("bmp", @"D:\out\photo_20261013_153012.bmp")] // 先頭の . は無くてもよい
    public void CreatePath_UsesBaseNameTimestampAndExtension(string extension, string expected)
    {
        var namer = new OutputFileNamer(_ => false);

        Assert.Equal(expected, namer.CreatePath(@"D:\out", @"C:\a\photo.heic", Now, extension));
    }

    [Fact]
    public void CreatePath_AppendsSequenceWhenExists()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\out\photo_20261013_153012.png",
        };
        var namer = new OutputFileNamer(existing.Contains);

        Assert.Equal(@"D:\out\photo_20261013_153012_2.png", namer.CreatePath(@"D:\out", @"C:\a\photo.jpg", Now, ".png"));

        existing.Add(@"D:\out\photo_20261013_153012_2.png");
        Assert.Equal(@"D:\out\photo_20261013_153012_3.png", namer.CreatePath(@"D:\out", @"C:\a\photo.jpg", Now, ".png"));

        // 拡張子が違えば重ならない
        Assert.Equal(@"D:\out\photo_20261013_153012.jpg", namer.CreatePath(@"D:\out", @"C:\a\photo.jpg", Now, ".jpg"));
    }

    [Fact]
    public void CreatePath_WithRealFileSystem_AvoidsExistingFileAndFolder()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.Combine("photo_20261013_153012.png"), "x");
        Directory.CreateDirectory(temp.Combine("photo_20261013_153012_2.png")); // 同じ名前のフォルダも避ける
        var namer = new OutputFileNamer();

        var path = namer.CreatePath(temp.Path, @"C:\a\photo.jpg", Now, ".png");

        Assert.Equal(temp.Combine("photo_20261013_153012_3.png"), path);
    }

    [Fact]
    public void CreatePath_SourceNameWithDotsAndNoName()
    {
        var namer = new OutputFileNamer(_ => false);

        // 最後の拡張子だけを除く
        Assert.Equal(@"D:\out\my.photo.v2_20261013_153012.jpg", namer.CreatePath(@"D:\out", @"C:\a\my.photo.v2.png", Now, ".jpg"));
        // 名前が無いときは image
        Assert.Equal(@"D:\out\image_20261013_153012.jpg", namer.CreatePath(@"D:\out", @"C:\a\.png", Now, ".jpg"));
    }

    [Fact]
    public void CreatePath_UsesInvariantDigitsRegardlessOfCulture()
    {
        var namer = new OutputFileNamer(_ => false);
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.EndsWith("photo_20261013_153012.png", namer.CreatePath(@"D:\out", @"C:\a\photo.png", Now, ".png"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        var namer = new OutputFileNamer(_ => false);

        Assert.Throws<ArgumentException>(() => namer.CreatePath("", @"C:\a\photo.png", Now, ".jpg"));
        Assert.Throws<ArgumentException>(() => namer.CreatePath(@"D:\out", "", Now, ".jpg"));
        Assert.Throws<ArgumentException>(() => namer.CreatePath(@"D:\out", @"C:\a\photo.png", Now, ""));
        Assert.Throws<ArgumentNullException>(() => new OutputFileNamer(null!));
    }
}
