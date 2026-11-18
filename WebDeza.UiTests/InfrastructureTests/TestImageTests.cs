using System.Windows.Media.Imaging;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.InfrastructureTests;

/// <summary>テスト画像を作ること（作り直すのは、無いときと版が変わったときだけ）</summary>
public class TestImageTests
{
    [Fact]
    public void Ensure_CreatesOnlyWhenMissingOrVersionChanged()
    {
        using var temp = new TempFolder();
        string root = temp.Combine("images");
        int calls = 0;
        void Generate(string dir)
        {
            calls++;
            File.WriteAllText(Path.Combine(dir, $"v{calls}.txt"), "");
        }

        Assert.True(TestImageCache.Ensure(root, "1", Generate));
        Assert.False(TestImageCache.Ensure(root, "1", Generate));
        Assert.Equal(1, calls);
        Assert.True(File.Exists(Path.Combine(root, "v1.txt")));

        // 版が変わったら作り直し、前の画像は残さない
        Assert.True(TestImageCache.Ensure(root, "2", Generate));
        Assert.Equal(2, calls);
        Assert.False(File.Exists(Path.Combine(root, "v1.txt")));
        Assert.True(File.Exists(Path.Combine(root, "v2.txt")));
        Assert.Equal("2", File.ReadAllText(Path.Combine(root, TestImageCache.StampFileName)));
    }

    [Fact]
    public void Ensure_GenerateFails_LeavesPreviousImages()
    {
        using var temp = new TempFolder();
        string root = temp.Combine("images");
        TestImageCache.Ensure(root, "1", dir => File.WriteAllText(Path.Combine(dir, "old.txt"), ""));

        Assert.Throws<IOException>(() => TestImageCache.Ensure(root, "2", dir =>
        {
            File.WriteAllText(Path.Combine(dir, "half.txt"), "");
            throw new IOException("途中で失敗");
        }));

        Assert.True(File.Exists(Path.Combine(root, "old.txt")));
        Assert.Equal("1", File.ReadAllText(Path.Combine(root, TestImageCache.StampFileName)));
        Assert.False(Directory.Exists(root + ".tmp"));
    }

    [Fact]
    public void Generate_FormatsExifBroken_CreatesExpectedFiles()
    {
        using var temp = new TempFolder();

        TestImageFactory.Generate(temp.Path, ImageSet.Formats | ImageSet.Exif | ImageSet.Broken);

        foreach (var name in new[] { "sample.jpg", "sample.png", "sample.bmp", "sample.tif", "sample.gif", "sample.jxr" })
        {
            var pixels = Pixels.Load(temp.Combine("formats", name));
            Assert.Equal((1200, 900), (pixels.Width, pixels.Height));
        }
        var png = Pixels.Load(temp.Combine("formats", "sample.png"));
        Assert.Equal(ArrowDirection.Up, png.Arrow(out var detail));
        Assert.True(Pixels.ThumbDiff(png, Pixels.Load(temp.Combine("formats", "sample.jpg"))) < 10, detail);
        Assert.Equal((800, 600), Size(Pixels.Load(temp.Combine("formats", "png_content.jpg"))));
        Assert.True(File.Exists(temp.Combine("formats", "sample.ico")));
        Assert.True(File.Exists(temp.Combine("formats", "not_image.txt")));

        // EXIF の向き 6: 画素は横向き（1200×900）で、矢印は横を向いている
        string exif = temp.Combine("exif", "portrait_orientation6.jpg");
        var stored = Pixels.Load(exif);
        Assert.Equal((1200, 900), Size(stored));
        Assert.Equal(ArrowDirection.Left, stored.Arrow(out _));
        Assert.Equal(6, ExifOrientation(exif));

        Assert.Equal("not a jpeg\n", File.ReadAllText(temp.Combine("broken", "broken1.jpg")));
        Assert.Equal((800, 600), Size(Pixels.Load(temp.Combine("broken", "ok.jpg"))));
    }

    private static (int, int) Size(Pixels p) => (p.Width, p.Height);

    private static int ExifOrientation(string path)
    {
        int value = 0;
        var thread = new Thread(() =>
        {
            using var fs = File.OpenRead(path);
            var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            value = Convert.ToInt32(((BitmapMetadata)frame.Metadata).GetQuery("/app1/ifd/{ushort=274}"));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return value;
    }
}
