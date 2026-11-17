using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Imaging.Wic;
using WebDeza.Services;

namespace WebDeza.Tests.Services;

/// <summary>保存サイズの計算（設計書 F-09・5.6・5.8、V05〜V07。実装計画書 10-5）</summary>
public sealed class ExportPreviewServiceTests
{
    private static readonly RgbColor White = new(255, 255, 255);

    private readonly FormatRegistry _formats = new();
    private readonly FakeEncoderFormat _fake = new("fakejpeg", alpha: false, ".fjpg");
    private readonly ExportPreviewService _service;

    public ExportPreviewServiceTests()
    {
        _formats.AddWicFormats();
        _formats.Register(_fake);
        _service = new ExportPreviewService(_formats);
    }

    private static ExportSettings Settings(string formatId, int? quality = null, RgbColor? background = null, long? sizeLimit = null)
    {
        var map = new Dictionary<string, ParameterValues>();
        if (quality is { } q) map[formatId] = ParameterValues.Empty.With(FakeEncoderFormat.QualityKey, (long)q);
        return new ExportSettings(formatId, map, sizeLimit, background ?? White);
    }

    /// <summary>半透明の画素を含む 16×8 の画像</summary>
    private static ImageBuffer Translucent()
    {
        var pixels = new byte[16 * 8 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 4 == 3 ? 255 : i * 7 % 251);
        pixels[3] = 0;
        return ImageBuffer.Create(16, 8, pixels);
    }

    // ---------------------------------------------------------------- V05: 保存する画像の使い回し

    [Fact]
    public async Task OnlyQualityChanges_RendersOnce_EncodesEachTime()
    {
        // V05: 品質だけを 3 回変える → 保存する画像を作るのは 1 回、エンコードは 3 回
        var image = Translucent();

        var first = await _service.RequestAsync(image, Settings(_fake.Id, 80));
        var second = await _service.RequestAsync(image, Settings(_fake.Id, 50));
        var third = await _service.RequestAsync(image, Settings(_fake.Id, 20));

        Assert.Equal(1, _service.RenderCount);
        Assert.Equal(3, _service.EncodeCount);
        Assert.Same(first!.Rendered, third!.Rendered); // 背景色で塗った画像を使い回す
        Assert.Equal("FAKE 16x8 q=20", System.Text.Encoding.ASCII.GetString(third.Encoded.Bytes));
        Assert.Equal(50, second!.Encoded.UsedOptions.Get<int>(FakeEncoderFormat.QualityKey));
    }

    [Fact]
    public async Task BackgroundChange_RendersAgain()
    {
        // V05: 背景色を変える → 保存する画像を作り直す
        var image = Translucent();
        var white = await _service.RequestAsync(image, Settings(_fake.Id, 80));

        var black = await _service.RequestAsync(image, Settings(_fake.Id, 80, new RgbColor(0, 0, 0)));

        Assert.Equal(2, _service.RenderCount);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, white!.Rendered.Pixels[0..4]); // 透明な画素は背景色
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, black!.Rendered.Pixels[0..4]);
    }

    [Fact]
    public async Task RenderedBitmap_IsReusedWithRenderedImage()
    {
        // 圧縮前の描画用データも、保存する画像を使い回すときは使い回す（品質のスライダーで作り直さない）
        var image = Translucent();
        var first = await _service.RequestAsync(image, Settings(_fake.Id, 80));
        var second = await _service.RequestAsync(image, Settings(_fake.Id, 40));

        Assert.NotNull(first!.RenderedBitmap);
        Assert.True(first.RenderedBitmap.IsFrozen);
        Assert.Equal((16, 8), (first.RenderedBitmap.PixelWidth, first.RenderedBitmap.PixelHeight));
        Assert.Same(first.RenderedBitmap, second!.RenderedBitmap);

        var black = await _service.RequestAsync(image, Settings(_fake.Id, 40, new RgbColor(0, 0, 0)));
        Assert.NotSame(first.RenderedBitmap, black!.RenderedBitmap); // 背景色が変われば作り直す
    }

    [Fact]
    public async Task ImageOrAlphaSupportChange_RendersAgain()
    {
        var image = Translucent();
        await _service.RequestAsync(image, Settings(_fake.Id));

        await _service.RequestAsync(Translucent(), Settings(_fake.Id));      // 別の画像（編集した後など）
        Assert.Equal(2, _service.RenderCount);

        var png = await _service.RequestAsync(image, Settings(WicFormats.PngId)); // 透過を保てる形式
        Assert.Equal(3, _service.RenderCount);
        Assert.Same(image, png!.Rendered); // 塗らずにそのまま
    }

    [Fact]
    public async Task SameRequest_ReusesLatestResult()
    {
        var image = Translucent();
        var first = await _service.RequestAsync(image, Settings(_fake.Id, 80));
        var completed = new List<ExportPreviewResult>();
        _service.Completed += (_, r) => completed.Add(r);

        var again = await _service.RequestAsync(image, Settings(_fake.Id, 80)); // 中身が同じ設定

        Assert.Same(first, again);
        Assert.Equal(1, _service.EncodeCount); // 計算し直さない
        Assert.Same(first, Assert.Single(completed));
    }

    [Fact]
    public async Task Result_HasSizeAndConditions()
    {
        var image = Translucent();
        var settings = Settings(_fake.Id, 80);

        var result = await _service.RequestAsync(image, settings);

        Assert.Equal((16, 8), (result!.Width, result.Height));
        Assert.Equal(result.Encoded.Bytes.LongLength, result.FileSize);
        Assert.Same(image, result.Source);
        Assert.True(result.Matches(image, Settings(_fake.Id, 80)));
        Assert.False(result.Matches(image, Settings(_fake.Id, 79)));
        Assert.False(result.Matches(Translucent(), settings));
        Assert.Same(result, _service.Latest);
    }

    [Fact]
    public async Task SizeLimit_IsSearched_AsWhenSaving()
    {
        var result = await _service.RequestAsync(Translucent(), Settings(_fake.Id, sizeLimit: 14)); // 「FAKE 16x8 q=NN」は 14 バイト

        Assert.Equal(99, result!.Encoded.UsedOptions.Get<int>(FakeEncoderFormat.QualityKey));
        Assert.True(result.Encoded.FitsSizeLimit);
    }

    // ---------------------------------------------------------------- V06: 古い計算の結果を捨てる

    [Fact]
    public async Task NewRequestWhileComputing_OnlyLastResultIsNotified()
    {
        // V06: 計算中に次の依頼が来る → 古い結果は通知されず、最後の依頼の結果だけが通知される
        var image = Translucent();
        var gate = new ManualResetEventSlim();
        var entered = new ManualResetEventSlim();
        _fake.OnEncode = () =>
        {
            if (!gate.IsSet)
            {
                entered.Set();
                gate.Wait(TimeSpan.FromSeconds(10));
            }
        };
        var completed = new List<ExportPreviewResult>();
        _service.Completed += (_, r) => completed.Add(r);

        var old = _service.RequestAsync(image, Settings(_fake.Id, 80));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10))); // 古い依頼がエンコード中
        var latest = _service.RequestAsync(image, Settings(_fake.Id, 30));
        gate.Set();

        Assert.Null(await old); // 古い結果は捨てる
        var result = await latest;

        var notified = Assert.Single(completed);
        Assert.Same(result, notified);
        Assert.Equal(30, notified.Encoded.UsedOptions.Get<int>(FakeEncoderFormat.QualityKey));
        Assert.Same(result, _service.Latest);
    }

    [Fact]
    public async Task Cancel_DiscardsComputingResult()
    {
        var gate = new ManualResetEventSlim();
        var entered = new ManualResetEventSlim();
        _fake.OnEncode = () => { entered.Set(); gate.Wait(TimeSpan.FromSeconds(10)); };
        var completed = 0;
        _service.Completed += (_, _) => completed++;

        var computing = _service.RequestAsync(Translucent(), Settings(_fake.Id));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        _service.Cancel();
        gate.Set();

        Assert.Null(await computing);
        Assert.Equal(0, completed);
        Assert.Null(_service.Latest);
    }

    [Fact]
    public async Task Failure_RaisesFailed_AndClearsLatest()
    {
        // 形式プラグインの例外でも落とさない。表示を「—」にするのは受ける側（設計書 5.10-6）
        var image = Translucent();
        await _service.RequestAsync(image, Settings(_fake.Id, 80));
        _fake.Throw = () => new InvalidOperationException("壊れた");
        var failures = new List<Exception>();
        _service.Failed += (_, ex) => failures.Add(ex);

        var result = await _service.RequestAsync(image, Settings(_fake.Id, 70));

        Assert.Null(result);
        Assert.Equal("壊れた", Assert.Single(failures).Message);
        Assert.Null(_service.Latest);
    }

    [Fact]
    public async Task UnsupportedFormat_RaisesFailed()
    {
        var failures = new List<Exception>();
        _service.Failed += (_, ex) => failures.Add(ex);

        Assert.Null(await _service.RequestAsync(Translucent(), Settings(WicFormats.GifId))); // 保存できない形式

        Assert.IsType<UnsupportedImageFormatException>(Assert.Single(failures));
    }

    // ---------------------------------------------------------------- V07: 圧縮後の画像

    [Theory]
    [InlineData(WicFormats.JpegId)]
    [InlineData(WicFormats.PngId)]
    [InlineData(WicFormats.BmpId)]
    [InlineData(WicFormats.TiffId)]
    public async Task ReadableFormat_HasDecoded(string formatId)
    {
        // V07: 読み直せる形式 → Decoded がある
        var image = Translucent();

        var result = await _service.RequestAsync(image, Settings(formatId));

        Assert.NotNull(result!.Decoded);
        Assert.Equal((16, 8), (result.Decoded.Width, result.Decoded.Height));
        if (formatId != WicFormats.JpegId) // 可逆の形式なら、保存する画像と画素が一致する
            Assert.Equal(result.Rendered.Pixels, result.Decoded.Pixels);
    }

    [Fact]
    public async Task UnreadableFormat_HasNoDecoded()
    {
        // V07: 読み直せない形式（デコーダーの無い偽の形式）→ Decoded が null（M27）
        var result = await _service.RequestAsync(Translucent(), Settings(_fake.Id));

        Assert.NotNull(result);
        Assert.Null(result.Decoded);
        Assert.True(result.FileSize > 0); // サイズは出せる
    }

    [Fact]
    public async Task JpegDecoded_ShowsCompressionLoss()
    {
        // 圧縮後は、エンコードしたバイト列を読み直したもの（品質を下げると劣化が見える）
        var pixels = new byte[64 * 64 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 4 == 3 ? 255 : (i * 37) % 256);
        var image = ImageBuffer.Create(64, 64, pixels);

        var low = await _service.RequestAsync(image, Settings(WicFormats.JpegId, 5));
        var high = await _service.RequestAsync(image, Settings(WicFormats.JpegId, 100));

        static long Diff(ImageBuffer a, ImageBuffer b) => a.Pixels.Zip(b.Pixels).Sum(p => (long)Math.Abs(p.First - p.Second));
        Assert.True(Diff(image, low!.Decoded!) > Diff(image, high!.Decoded!));
        Assert.True(low.FileSize < high.FileSize);
    }
}
