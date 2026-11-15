using WebDeza.Composition;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Services;

namespace WebDeza.Tests.Services;

public class OpenFileFilterTests
{
    [Fact]
    public void Build_StandardFormats_ListsAllExtensionsThenEachFormat()
    {
        var formats = new FormatRegistry();
        AppModules.Register(formats, new FilterRegistry(), new WebDeza.Views.Canvas.ToolRegistry());

        var parts = OpenFileFilter.Build(formats).Split('|');

        // 「表示名|パターン」の組が、画像ファイル・10 形式・すべてのファイルの 12 組
        Assert.Equal(24, parts.Length);
        Assert.Equal("画像ファイル", parts[0]);
        Assert.Equal("*.jpg;*.jpeg;*.jpe;*.jfif;*.png;*.bmp;*.dib;*.tif;*.tiff;*.gif;*.jxr;*.wdp;*.ico;*.webp;*.heic;*.heif;*.avif", parts[1]);
        Assert.Equal(("JPEG (*.jpg;*.jpeg;*.jpe;*.jfif)", "*.jpg;*.jpeg;*.jpe;*.jfif"), (parts[2], parts[3]));
        Assert.Equal(("AVIF (*.avif)", "*.avif"), (parts[20], parts[21]));
        Assert.Equal(("すべてのファイル", "*.*"), (parts[22], parts[23]));
    }

    [Fact]
    public void Build_AddedFormat_AppearsWithoutChangingDialogCode()
    {
        var formats = new FormatRegistry();
        formats.Register(new FakeFormat("fake", ".fake", ".fk") { DisplayName = "偽の形式" });

        Assert.Equal("画像ファイル|*.fake;*.fk|偽の形式 (*.fake;*.fk)|*.fake;*.fk|すべてのファイル|*.*", OpenFileFilter.Build(formats));
    }

    [Fact]
    public void Build_NoFormats_OnlyAllFiles()
    {
        Assert.Equal("すべてのファイル|*.*", OpenFileFilter.Build(new FormatRegistry()));
    }
}
