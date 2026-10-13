using System.IO;
using System.Windows.Media.Imaging;
using CoverCropper.Services;

namespace CoverCropper.Tests;

public class JpegSaverTests
{
    [Fact] // U13
    public void Save_WritesJpeg1024x768WithoutTempFiles()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("out.jpg");
        var image = new ImageCropper().Crop(TestImages.CreateGradient(2304, 768), 640, 0);

        new JpegSaver().Save(image, path, AppConstants.JpegQuality);

        using (var stream = File.OpenRead(path))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert.IsType<JpegBitmapDecoder>(decoder);
            Assert.Equal(1024, decoder.Frames[0].PixelWidth);
            Assert.Equal(768, decoder.Frames[0].PixelHeight);
        }
        Assert.Equal(new[] { path }, Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Save_ExistingFile_IsNotOverwritten()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("exists.jpg");
        File.WriteAllText(path, "original");
        var image = TestImages.CreateGradient(1024, 768);

        Assert.Throws<IOException>(() => new JpegSaver().Save(image, path, AppConstants.JpegQuality));

        Assert.Equal("original", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Save_QualityAffectsFileSize()
    {
        using var temp = new TempFolder();
        var image = TestImages.CreateGradient(1024, 768);
        var low = temp.Combine("q30.jpg");
        var high = temp.Combine("q85.jpg");

        new JpegSaver().Save(image, low, 30);
        new JpegSaver().Save(image, high, 85);

        Assert.True(new FileInfo(high).Length > new FileInfo(low).Length);
    }
}
