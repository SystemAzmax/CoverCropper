using System.Globalization;
using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Imaging.Wic;
using WebDeza.Services;
using WebDeza.ViewModels;

namespace WebDeza.Tests.ViewModels;

/// <summary>保存のダイアログの保存サイズの表示とプレビュー（設計書 F-09、V04。実装計画書 10-6・10-8）</summary>
public sealed class ExportDialogSizeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan ShortInterval = TimeSpan.FromMilliseconds(10);

    private readonly TempFolder _temp = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FormatRegistry _formats = new();
    private readonly FakeEncoderFormat _fake = new("fakejpeg", alpha: false, ".fjpg");
    private readonly string _source;
    private readonly string _output;

    public ExportDialogSizeTests()
    {
        _formats.AddWicFormats();
        _formats.Register(_fake);
        _source = _temp.Combine("photo.png");
        File.WriteAllBytes(_source, [1, 2, 3]);
        _output = _temp.Combine("out");
        Directory.CreateDirectory(_output);
    }

    public void Dispose() => _temp.Dispose();

    private string LogFile => _temp.Combine("logs", "app.log");

    private DocumentViewModel CreateDocument(long sourceFileSize = 1000)
    {
        var pixels = new byte[40 * 30 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 4 == 3 ? 255 : i * 5 % 251);
        var image = ImageBuffer.Create(40, 30, pixels);
        return new DocumentViewModel(_source, image, DisplayImage.Create(image), sourceFileSize, (_, _) => null, _dialogs,
            new AppLog(LogFile));
    }

    private static ExportSettings Settings(string formatId)
        => new(formatId, new Dictionary<string, ParameterValues>(), null, new RgbColor(255, 255, 255));

    private ExportDialogViewModel CreateDialog(DocumentViewModel document, string formatId, TimeSpan? interval = null)
        => new(document, _output, Settings(formatId), new ExportService(_formats, new ManualTimeProvider(Now)), _dialogs,
            new AppLog(LogFile), interval ?? ShortInterval);

    private static NumericParameterViewModel Quality(ExportDialogViewModel dialog) => (NumericParameterViewModel)dialog.Options[0];

    // ---------------------------------------------------------------- 表示

    [Fact]
    public async Task Opening_CalculatesSize()
    {
        var document = CreateDocument(sourceFileSize: 3000);
        var dialog = CreateDialog(document, WicFormats.JpegId);
        Assert.True(dialog.IsCalculatingSize);
        Assert.Equal(ExportDialogViewModel.CalculatingText, dialog.FileSizeText); // 「計算中…」
        Assert.Null(dialog.FileSizeToolTip);

        await dialog.WaitForSizeAsync();

        var result = dialog.SizeResult!;
        Assert.False(dialog.IsCalculatingSize);
        Assert.Equal(ExportDialogViewModel.FormatFileSize(result.FileSize, 3000), dialog.FileSizeText);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, "{0:N0} バイト", result.FileSize), dialog.FileSizeToolTip);
        Assert.True(result.Matches(document.Image, dialog.Settings));
    }

    [Theory]
    [InlineData(186_778, 3_000_000, "182.4 KB（元ファイルの 6%）")]
    [InlineData(186_778, 0, "182.4 KB")]                 // 元ファイルのバイト数が分からない
    [InlineData(1_000, 200_000, "1000 B（元ファイルの 0.5%）")]
    [InlineData(3_000_000, 1_000_000, "2.86 MB（元ファイルの 300%）")]
    [InlineData(1_000, 100_000, "1000 B（元ファイルの 1%）")]      // 1% 以上は整数
    [InlineData(1_050, 100_000, "1.0 KB（元ファイルの 1%）")]       // 1.05% は 1%
    [InlineData(1_000, 10_000, "1000 B（元ファイルの 10%）")]
    public void FormatFileSize_ShowsRatioToSource(long bytes, long source, string expected)
        => Assert.Equal(expected, ExportDialogViewModel.FormatFileSize(bytes, source));

    [Fact]
    public async Task ChangingSettings_Recalculates()
    {
        var dialog = CreateDialog(CreateDocument(), WicFormats.JpegId);
        await dialog.WaitForSizeAsync();
        var first = dialog.SizeResult!;

        Quality(dialog).SliderValue = 10;
        Assert.Equal(ExportDialogViewModel.CalculatingText, dialog.FileSizeText); // 変えたらすぐ「計算中…」
        await dialog.WaitForSizeAsync();
        var lowQuality = dialog.SizeResult!;
        Assert.Equal(10, lowQuality.Encoded.UsedOptions.Get<int>(WicFormats.QualityKey));
        Assert.NotEqual(first.FileSize, lowQuality.FileSize);

        dialog.SelectedFormat = dialog.Formats.Single(f => f.Id == WicFormats.PngId); // 形式
        await dialog.WaitForSizeAsync();
        Assert.Equal(WicFormats.PngId, dialog.SizeResult!.Encoded.FormatId);

        dialog.Background.Text = "#000000"; // 背景色
        await dialog.WaitForSizeAsync();
        Assert.Equal(new RgbColor(0, 0, 0), dialog.SizeResult!.Settings.Background);
    }

    [Fact]
    public async Task ChangesInARow_AreThinned_AndLastValueWins()
    {
        // スライダーの操作中は間引いて計算し、止まったら必ず最後の値で計算する
        var dialog = CreateDialog(CreateDocument(), _fake.Id, TimeSpan.FromMilliseconds(150));
        await dialog.WaitForSizeAsync();
        int before = _fake.Calls.Count;

        for (int q = 50; q < 60; q++) Quality(dialog).SliderValue = q; // 続けて 10 回変える
        await dialog.WaitForSizeAsync();

        int encodes = _fake.Calls.Count - before;
        Assert.InRange(encodes, 1, 2); // 10 回ではなく、まとめて計算する
        Assert.Equal(59, dialog.SizeResult!.Encoded.UsedOptions.Get<int>(FakeEncoderFormat.QualityKey));
        Assert.Equal("FAKE 40x30 q=59", System.Text.Encoding.ASCII.GetString(dialog.SizeResult.Encoded.Bytes));
    }

    [Fact]
    public async Task Failure_ShowsDash_AndWritesLog()
    {
        _fake.Throw = () => new InvalidOperationException("壊れた");

        var dialog = CreateDialog(CreateDocument(), _fake.Id);
        await dialog.WaitForSizeAsync();

        Assert.Equal(ExportDialogViewModel.UnknownSizeText, dialog.FileSizeText); // 「—」
        Assert.Null(dialog.FileSizeToolTip);
        Assert.False(dialog.IsCalculatingSize);
        Assert.Null(dialog.SizeResult);
        Assert.Empty(_dialogs.Errors); // ダイアログは出さない
        Assert.Contains("Export size failed", File.ReadAllText(LogFile));
    }

    [Fact]
    public async Task Closed_StopsUpdating()
    {
        var dialog = CreateDialog(CreateDocument(), _fake.Id);
        await dialog.WaitForSizeAsync();
        var shown = dialog.FileSizeText;
        int before = _fake.Calls.Count;

        dialog.OnClosed();
        Quality(dialog).SliderValue = 10;
        await dialog.WaitForSizeAsync();

        Assert.Equal(shown, dialog.FileSizeText);
        Assert.Equal(before, _fake.Calls.Count); // 閉じた後は計算しない
    }

    // ---------------------------------------------------------------- プレビュー（10-8）

    [Theory] // 追加: 見出しの文
    [InlineData("JPEG", 72, 203_366, "JPEG 品質 72 ／ 198.6 KB")]
    [InlineData("PNG", null, 1_258_291, "PNG ／ 1.20 MB")]
    [InlineData("BMP", null, 900, "BMP ／ 900 B")]
    public void FormatPreviewHeadline_ShowsFormatQualityAndSize(string format, int? quality, long bytes, string expected)
        => Assert.Equal(expected, ExportDialogViewModel.FormatPreviewHeadline(format, quality, bytes));

    [Fact]
    public async Task Preview_ShowsDecodedImage_AndHeadline()
    {
        var dialog = CreateDialog(CreateDocument(), WicFormats.JpegId);
        Assert.Equal(ExportDialogViewModel.CalculatingText, dialog.PreviewHeadline);
        Assert.Null(dialog.PreviewImage);

        await dialog.WaitForSizeAsync();

        var result = dialog.SizeResult!;
        Assert.Equal($"JPEG 品質 85 ／ {FileSizeFormatter.Format(result.FileSize)}", dialog.PreviewHeadline);
        Assert.NotNull(dialog.PreviewImage);
        Assert.Equal((40, 30), (dialog.PreviewImage.PixelWidth, dialog.PreviewImage.PixelHeight));
        Assert.True(dialog.PreviewImage.IsFrozen); // バックグラウンドで作ったもの
        Assert.Same(result.DecodedBitmap, dialog.PreviewImage);
        Assert.Null(dialog.PreviewMessage);
        Assert.False(dialog.HasPreviewMessage);
    }

    [Fact]
    public async Task Preview_FormatWithoutQuality_HasNoQualityInHeadline()
    {
        var dialog = CreateDialog(CreateDocument(), WicFormats.PngId);
        await dialog.WaitForSizeAsync();

        Assert.Equal($"PNG ／ {FileSizeFormatter.Format(dialog.SizeResult!.FileSize)}", dialog.PreviewHeadline);
        Assert.NotNull(dialog.PreviewImage);
    }

    [Fact]
    public async Task Preview_UnreadableFormat_ShowsM27()
    {
        // 追加: 読み直せない形式は M27（サイズは出す）
        var dialog = CreateDialog(CreateDocument(), _fake.Id);
        await dialog.WaitForSizeAsync();

        Assert.Null(dialog.PreviewImage);
        Assert.Equal(Messages.M27_CannotDecodeResult, dialog.PreviewMessage);
        Assert.True(dialog.HasPreviewMessage);
        Assert.Equal("Fake fakejpeg 品質 85 ／ 15 B", dialog.PreviewHeadline);
        Assert.Equal(ExportDialogViewModel.FormatFileSize(15, 1000), dialog.FileSizeText);

        dialog.SelectedFormat = dialog.Formats.Single(f => f.Id == WicFormats.JpegId); // 読み直せる形式にすると消える
        await dialog.WaitForSizeAsync();
        Assert.Null(dialog.PreviewMessage);
        Assert.NotNull(dialog.PreviewImage);
    }

    [Fact]
    public async Task Preview_WhileRecalculating_KeepsPreviousImage()
    {
        var dialog = CreateDialog(CreateDocument(), WicFormats.JpegId);
        await dialog.WaitForSizeAsync();
        var image = dialog.PreviewImage;

        Quality(dialog).SliderValue = 20;

        Assert.Equal(ExportDialogViewModel.CalculatingText, dialog.PreviewHeadline);
        Assert.Same(image, dialog.PreviewImage); // ちらつかないよう、新しい結果が出るまで前のものを出す
        await dialog.WaitForSizeAsync();
        Assert.NotSame(image, dialog.PreviewImage);
        Assert.StartsWith("JPEG 品質 20 ／ ", dialog.PreviewHeadline);
    }

    [Fact]
    public async Task Preview_Failure_ClearsImage()
    {
        var dialog = CreateDialog(CreateDocument(), WicFormats.JpegId);
        await dialog.WaitForSizeAsync();
        _fake.Throw = () => new InvalidOperationException("壊れた");

        dialog.SelectedFormat = dialog.Formats.Single(f => f.Id == _fake.Id);
        await dialog.WaitForSizeAsync();

        Assert.Equal(ExportDialogViewModel.UnknownSizeText, dialog.PreviewHeadline);
        Assert.Null(dialog.PreviewImage);
        Assert.Null(dialog.PreviewMessage);
    }

    // ---------------------------------------------------------------- V04: 計算したバイト列をそのまま書く

    [Fact]
    public async Task SaveAfterCalculation_WritesCalculatedBytes_WithoutEncodingAgain()
    {
        // V04: 保存サイズを計算した直後に保存する → ファイルの中身が計算したバイト列と一致（エンコードは 1 回だけ）
        var dialog = CreateDialog(CreateDocument(), _fake.Id);
        await dialog.WaitForSizeAsync();
        var computed = dialog.SizeResult!;
        Assert.Single(_fake.Calls);

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Single(_fake.Calls); // 保存のときにエンコードし直さない
        Assert.Equal(computed.Encoded.Bytes, File.ReadAllBytes(dialog.Result!.Path));
        Assert.Same(computed.Encoded, dialog.Result.Encoded);
    }

    [Fact]
    public async Task ShownSize_EqualsSavedFileSize()
    {
        // T14: 表示と、保存したファイルのバイト数が一致する（本物の JPEG で）
        var dialog = CreateDialog(CreateDocument(), WicFormats.JpegId);
        Quality(dialog).SliderValue = 72;
        await dialog.WaitForSizeAsync();
        var toolTip = dialog.FileSizeToolTip;

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal(string.Format(CultureInfo.InvariantCulture, "{0:N0} バイト", new FileInfo(dialog.Result!.Path).Length), toolTip);
    }

    [Fact]
    public async Task SaveWhileCalculating_StillSaves()
    {
        // 計算中でも［保存］は押せる（保存の処理でエンコードし直す）
        var gate = new ManualResetEventSlim();
        int calls = 0;
        _fake.OnEncode = () => { if (Interlocked.Increment(ref calls) == 1) gate.Wait(TimeSpan.FromSeconds(10)); };
        var dialog = CreateDialog(CreateDocument(), _fake.Id);
        Assert.True(dialog.IsCalculatingSize);
        Assert.True(dialog.SaveCommand.CanExecute(null));

        await dialog.SaveCommand.ExecuteAsync(null);
        gate.Set();

        Assert.NotNull(dialog.Result);
        Assert.Equal("FAKE 40x30 q=85", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(dialog.Result.Path)));
    }

    [Fact]
    public async Task SaveAfterChangingSettings_DoesNotUseOldBytes()
    {
        var dialog = CreateDialog(CreateDocument(), _fake.Id, TimeSpan.FromSeconds(10)); // 次の計算は 10 秒後まで頼まれない
        await dialog.WaitForSizeAsync();
        Quality(dialog).SliderValue = 40; // 計算はまだ（前の結果は品質 85）

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal("FAKE 40x30 q=40", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(dialog.Result!.Path)));
        dialog.OnClosed();
    }
}
