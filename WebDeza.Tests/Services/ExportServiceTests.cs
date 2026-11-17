using System.IO;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Imaging.Wic;
using WebDeza.Services;

namespace WebDeza.Tests.Services;

/// <summary>1 枚の保存（設計書 5.6・5.8・F-10、V01〜V03。実装計画書 10-1）</summary>
public sealed class ExportServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 30, 0, TimeSpan.Zero);
    private static readonly RgbColor White = new(255, 255, 255);

    private readonly TempFolder _temp = new();
    private readonly FormatRegistry _formats = new();
    private readonly FakeEncoderFormat _opaqueFormat = new("fakejpeg", alpha: false, ".fjpg", ".fjpeg");
    private readonly FakeEncoderFormat _alphaFormat = new("fakepng", alpha: true, ".fpng");
    private readonly string _source;
    private readonly string _output;

    public ExportServiceTests()
    {
        _formats.AddWicFormats();
        _formats.Register(_opaqueFormat);
        _formats.Register(_alphaFormat);
        _source = _temp.Combine("photo.png");
        File.WriteAllBytes(_source, [1, 2, 3]);
        _output = _temp.Combine("out");
        Directory.CreateDirectory(_output);
    }

    public void Dispose() => _temp.Dispose();

    private ExportService CreateService(OutputPathFactory? createPath = null)
        => new(_formats, new ManualTimeProvider(Now), createPath);

    private static ExportSettings Settings(string formatId, ParameterValues? options = null, long? sizeLimit = null,
        RgbColor? background = null)
    {
        var map = new Dictionary<string, ParameterValues>();
        if (options is not null) map[formatId] = options;
        return new ExportSettings(formatId, map, sizeLimit, background ?? White);
    }

    private static ImageBuffer Opaque(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 4 == 3 ? 255 : i * 7 % 251);
        return ImageBuffer.Create(width, height, pixels);
    }

    /// <summary>半透明の画素を含む画像</summary>
    private static ImageBuffer Translucent(int width, int height)
    {
        var pixels = Opaque(width, height).Pixels;
        pixels[3] = 0;
        pixels[7] = 128;
        return ImageBuffer.Create(width, height, pixels);
    }

    // ---------------------------------------------------------------- V01: 元画像を守る

    [Fact]
    public void Export_PathSameAsSource_DoesNotWrite()
    {
        // V01: 保存先が元画像と同じになる（ファイル名の生成を差し替え） → 書かない（例外）
        var service = CreateService((_, source, _, _) => source);

        Assert.Throws<InvalidOperationException>(() =>
            service.Export(Opaque(8, 4), _source, _temp.Path, Settings(_opaqueFormat.Id)));

        Assert.Equal([1, 2, 3], File.ReadAllBytes(_source)); // 元画像は変わらない
        Assert.Equal(["photo.png"], Directory.GetFiles(_temp.Path).Select(Path.GetFileName)); // 一時ファイルも残らない
    }

    [Fact]
    public void Export_ExistingFile_IsNotOverwritten()
    {
        var service = CreateService();
        var first = service.Export(Opaque(8, 4), _source, _output, Settings(_opaqueFormat.Id));

        var second = service.Export(Opaque(8, 4), _source, _output, Settings(_opaqueFormat.Id)); // 同じ日時

        Assert.Equal(Path.Combine(_output, "photo_20261117_103000.fjpg"), first.Path);
        Assert.Equal(Path.Combine(_output, "photo_20261117_103000_2.fjpg"), second.Path); // _2 を付ける
    }

    [Fact]
    public void Export_PathCollidingAfterNaming_ThrowsAndKeepsFile()
    {
        // 名前を決めた後に、ほかで同じ名前のファイルが作られたときも上書きしない
        var taken = Path.Combine(_output, "taken.fjpg");
        File.WriteAllBytes(taken, [9]);
        var service = CreateService((_, _, _, _) => taken);

        Assert.Throws<IOException>(() => service.Export(Opaque(8, 4), _source, _output, Settings(_opaqueFormat.Id)));
        Assert.Equal([9], File.ReadAllBytes(taken));
    }

    // ---------------------------------------------------------------- V02: 拡張子

    [Theory] // V02
    [InlineData(WicFormats.PngId, ".png")]
    [InlineData(WicFormats.TiffId, ".tif")]
    [InlineData(WicFormats.JpegId, ".jpg")]
    [InlineData(WicFormats.BmpId, ".bmp")]
    public void Export_UsesFirstExtensionOfFormat(string formatId, string extension)
    {
        var result = CreateService().Export(Opaque(16, 8), _source, _output, Settings(formatId));

        Assert.Equal(Path.Combine(_output, "photo_20261117_103000" + extension), result.Path);
        Assert.True(File.Exists(result.Path));
        var decoded = _formats.Decode(result.Path); // 読み直せる正しいファイル
        Assert.Equal((16, 8), (decoded.Image.Width, decoded.Image.Height));
    }

    [Fact]
    public void CreateOutputPath_UsesTimeAndFormatExtension()
    {
        var service = CreateService();

        Assert.Equal(Path.Combine(_output, "photo_20261117_103000.fpng"),
            service.CreateOutputPath(_output, _source, _alphaFormat.Id));
        Assert.False(File.Exists(Path.Combine(_output, "photo_20261117_103000.fpng"))); // 決めるだけで書かない
    }

    // ---------------------------------------------------------------- V03: オプション

    [Fact]
    public void Export_PassesQualityToEncoder_AndWritesItsBytes()
    {
        // V03: JPEG の品質 50 → エンコーダーに 50 が渡る（エンコーダーを差し替え）
        var options = ParameterValues.Empty.With(FakeEncoderFormat.QualityKey, 50L);

        var result = CreateService().Export(Opaque(8, 4), _source, _output, Settings(_opaqueFormat.Id, options));

        var call = Assert.Single(_opaqueFormat.Calls);
        Assert.Equal(50, call.Options.Get<int>(FakeEncoderFormat.QualityKey));
        Assert.Equal(result.Encoded.Bytes, File.ReadAllBytes(result.Path)); // エンコーダーが作ったバイト列をそのまま書く
        Assert.Equal(new FileInfo(result.Path).Length, result.FileSize);
        Assert.Equal(_opaqueFormat.Id, result.Encoded.FormatId);
        Assert.True(result.Encoded.FitsSizeLimit);
    }

    [Fact]
    public void Export_WithoutOptions_UsesEncoderDefaults()
    {
        CreateService().Export(Opaque(8, 4), _source, _output, Settings(_opaqueFormat.Id));

        Assert.Equal(85, _opaqueFormat.Calls[0].Options.Get<int>(FakeEncoderFormat.QualityKey));
    }

    [Fact]
    public void Export_WithSizeLimit_UsesFoundQuality()
    {
        // 品質が 2 桁の「FAKE 8x4 q=NN」（13 バイト）までに収める → 3 桁の 100（14 バイト）は収まらず、99 になる
        var result = CreateService().Export(Opaque(8, 4), _source, _output, Settings(_opaqueFormat.Id, sizeLimit: 13));

        Assert.Equal(99, result.Encoded.UsedOptions.Get<int>(FakeEncoderFormat.QualityKey));
        Assert.Equal("FAKE 8x4 q=99", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(result.Path)));
    }

    // ---------------------------------------------------------------- 透過と背景色

    [Fact]
    public void Export_TransparentImage_ToOpaqueFormat_IsFilledWithBackground()
    {
        var image = Translucent(8, 4);
        var black = new RgbColor(0, 0, 0);

        CreateService().Export(image, _source, _output, Settings(_opaqueFormat.Id, background: black));

        var encoded = _opaqueFormat.Calls[0].Image;
        Assert.True(encoded.IsOpaque);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, encoded.Pixels[0..4]); // 透明な画素は背景色（黒）
    }

    [Fact]
    public void Export_TransparentImage_ToAlphaFormat_KeepsAlpha()
    {
        var image = Translucent(8, 4);

        CreateService().Export(image, _source, _output, Settings(_alphaFormat.Id));

        Assert.Same(image, _alphaFormat.Calls[0].Image);
    }

    [Fact]
    public void Export_TransparentImage_ToPng_KeepsAlphaInFile()
    {
        var result = CreateService().Export(Translucent(8, 4), _source, _output, Settings(WicFormats.PngId));

        var decoded = _formats.Decode(result.Path).Image;
        Assert.Equal(0, decoded.Pixels[3]);
        Assert.Equal(128, decoded.Pixels[7]);
    }

    // ---------------------------------------------------------------- 失敗

    [Theory]
    [InlineData("unknown")]
    [InlineData(WicFormats.GifId)] // 読めるが保存できない
    public void Export_UnsupportedFormat_Throws_AndWritesNothing(string formatId)
    {
        Assert.Throws<UnsupportedImageFormatException>(() =>
            CreateService().Export(Opaque(8, 4), _source, _output, Settings(formatId)));
        Assert.Empty(Directory.GetFiles(_output));
    }

    [Fact]
    public void Export_MissingFolder_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            CreateService().Export(Opaque(8, 4), _source, _temp.Combine("none"), Settings(_opaqueFormat.Id)));
    }

    [Fact]
    public void Export_Cancelled_WritesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            CreateService().Export(Opaque(8, 4), _source, _output, Settings(_opaqueFormat.Id), cancellation.Token));
        Assert.Empty(Directory.GetFiles(_output));
    }

    [Fact]
    public void Encode_SameAsExportWithoutWriting()
    {
        var service = CreateService();
        var options = ParameterValues.Empty.With(FakeEncoderFormat.QualityKey, 40L);

        var encoded = service.Encode(Opaque(8, 4), Settings(_opaqueFormat.Id, options));

        Assert.Equal("FAKE 8x4 q=40", System.Text.Encoding.ASCII.GetString(encoded.Bytes));
        Assert.Empty(Directory.GetFiles(_output));
    }
}
