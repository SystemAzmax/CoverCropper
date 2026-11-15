using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

/// <summary>書き出しの設定（ExportSettings）</summary>
public class EditingModelTests
{
    private static ParameterValues Quality(int quality) => ParameterValues.Empty.With("quality", quality);

    private static ExportSettings JpegSettings(IReadOnlyDictionary<string, ParameterValues> options) => new(
        "jpeg", options, null, new RgbColor(255, 255, 255));

    // ---------------------------------------------------------------- E24: ExportSettings

    [Fact]
    public void ExportSettings_SameContentInDifferentDictionaries_IsEqual()
    {
        var a = JpegSettings(new Dictionary<string, ParameterValues>
        {
            ["jpeg"] = Quality(85),
            ["png"] = ParameterValues.Empty.With("interlace", false),
        });
        var b = JpegSettings(new Dictionary<string, ParameterValues>
        {
            ["png"] = ParameterValues.Empty.With("interlace", false),
            ["jpeg"] = Quality(85),
        });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ExportSettings_DifferentContent_IsNotEqual()
    {
        var settings = JpegSettings(new Dictionary<string, ParameterValues> { ["jpeg"] = Quality(85) });

        Assert.NotEqual(settings, settings with { FormatId = "png" });
        Assert.NotEqual(settings, settings.WithOptions("jpeg", Quality(70)));
        Assert.NotEqual(settings, settings.WithOptions("png", ParameterValues.Empty));
        Assert.NotEqual(settings, settings with { SizeLimitBytes = 300 * 1024 });
        Assert.NotEqual(settings, settings with { Background = new RgbColor(0, 0, 0) });
    }

    [Fact]
    public void ExportSettings_OptionsAreCopiedAndKeptPerFormat()
    {
        var options = new Dictionary<string, ParameterValues> { ["jpeg"] = Quality(85) };
        var settings = JpegSettings(options);
        options["jpeg"] = Quality(10);

        // 渡した辞書を後で変えても影響しない
        Assert.Equal(85, settings.GetOptions("jpeg").Get<int>("quality"));

        // 形式を切り替えても、それぞれの形式のオプションは残っている
        var png = settings with { FormatId = "png" };
        var backToJpeg = png.WithOptions("png", ParameterValues.Empty.With("interlace", true)) with { FormatId = "jpeg" };
        Assert.Equal(85, backToJpeg.GetOptions("jpeg").Get<int>("quality"));
        Assert.True(backToJpeg.GetOptions("png").Get<bool>("interlace"));
        Assert.Equal(ParameterValues.Empty, settings.GetOptions("bmp"));
    }

    [Fact]
    public void ExportSettings_ToString_ShowsOptionContents()
    {
        var settings = JpegSettings(new Dictionary<string, ParameterValues> { ["jpeg"] = Quality(85) });

        Assert.Contains("quality", settings.ToString());
        Assert.Contains("Background", settings.ToString());
    }
}
