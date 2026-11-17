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

/// <summary>保存のダイアログの品質の決め方とファイルサイズの上限（設計書 F-08・F-09、V23。実装計画書 10-7）</summary>
public sealed class ExportDialogSizeLimitTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 30, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FormatRegistry _formats = new();
    private readonly string _source;
    private readonly string _output;

    public ExportDialogSizeLimitTests()
    {
        _formats.AddWicFormats();
        _source = _temp.Combine("photo.png");
        File.WriteAllBytes(_source, [1, 2, 3]);
        _output = _temp.Combine("out");
        Directory.CreateDirectory(_output);
    }

    public void Dispose() => _temp.Dispose();

    private string LogFile => _temp.Combine("logs", "app.log");

    /// <summary>JPEG にしても小さくならない、ノイズの画像（400×300。品質 100 で数百 KB）</summary>
    private DocumentViewModel CreateNoisyDocument()
    {
        var pixels = new byte[400 * 300 * 4];
        new Random(1).NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var image = ImageBuffer.Create(400, 300, pixels);
        return new DocumentViewModel(_source, image, DisplayImage.Create(image), 100_000, (_, _) => null, _dialogs, new AppLog(LogFile));
    }

    private ExportDialogViewModel CreateDialog(DocumentViewModel document, long? sizeLimitBytes = null, long rememberedKB = 300,
        string formatId = WicFormats.JpegId)
        => new(document, _output, new ExportSettings(formatId, new Dictionary<string, ParameterValues>(), sizeLimitBytes,
                new RgbColor(255, 255, 255)),
            new ExportService(_formats, new ManualTimeProvider(Now)), _dialogs, new AppLog(LogFile),
            TimeSpan.FromMilliseconds(10), rememberedKB);

    private static NumericParameterViewModel Quality(ExportDialogViewModel dialog)
        => (NumericParameterViewModel)dialog.Options.Single(o => o.Key == WicFormats.QualityKey);

    private static IImageFormat Format(ExportDialogViewModel dialog, string id) => dialog.Formats.Single(f => f.Id == id);

    // ---------------------------------------------------------------- 品質の決め方

    [Fact]
    public void Initially_QualityMode_WithRememberedLimit()
    {
        var dialog = CreateDialog(CreateNoisyDocument(), rememberedKB: 250);

        Assert.True(dialog.CanUseSizeLimit); // JPEG はサイズを調整できる
        Assert.False(dialog.UseSizeLimit);
        Assert.True(dialog.UseQuality);
        Assert.False(dialog.IsSizeLimitActive);
        Assert.Equal("250", dialog.SizeLimitText); // 「品質を指定」でも、覚えている上限を出す
        Assert.Equal(250, dialog.SizeLimitKB);
        Assert.False(Quality(dialog).IsReadOnly);
        Assert.Null(dialog.Settings.SizeLimitBytes);
        dialog.OnClosed();
    }

    [Fact]
    public void OpenedWithLimit_StartsInLimitMode()
    {
        var dialog = CreateDialog(CreateNoisyDocument(), sizeLimitBytes: 120 * 1024);

        Assert.True(dialog.UseSizeLimit);
        Assert.Equal("120", dialog.SizeLimitText);
        Assert.True(Quality(dialog).IsReadOnly);
        Assert.Equal(120 * 1024, dialog.Settings.SizeLimitBytes);
        dialog.OnClosed();
    }

    [Fact]
    public async Task SizeLimitMode_MakesQualityReadOnly_AndShowsFoundQuality()
    {
        // V23: 品質の決め方を上限に → 品質の入力欄が読み取り専用
        var dialog = CreateDialog(CreateNoisyDocument());
        Quality(dialog).SliderValue = 77;
        await dialog.WaitForSizeAsync();
        dialog.SizeLimitText = "60";

        dialog.UseSizeLimit = true;

        Assert.True(Quality(dialog).IsReadOnly);
        Assert.False(Quality(dialog).IsEditable);
        Assert.False(dialog.UseQuality);
        Assert.True(dialog.IsSizeLimitActive);
        Assert.Equal(60 * 1024, dialog.Settings.SizeLimitBytes);
        await dialog.WaitForSizeAsync();

        var result = dialog.SizeResult!;
        int found = result.Encoded.UsedOptions.Get<int>(WicFormats.QualityKey);
        Assert.InRange(found, 1, 99);                     // 上限に収まる品質を探した
        Assert.True(result.FileSize <= 60 * 1024);
        Assert.Equal((long)found, Quality(dialog).Value);  // 探した品質を入力欄に出す
        Assert.Equal(ExportDialogViewModel.FormatPreviewHeadline("JPEG", found, result.FileSize), dialog.PreviewHeadline); // 見出しにも
        Assert.Null(dialog.SizeLimitWarning);
        Assert.False(dialog.IsCalculatingSize);            // 探した品質を出しても、計算し直さない
        Assert.Equal(77, dialog.Settings.GetOptions(WicFormats.JpegId).Get<int>(WicFormats.QualityKey)); // 設定は決めた品質のまま

        dialog.UseQuality = true; // 品質を指定に戻す

        Assert.False(Quality(dialog).IsReadOnly);
        Assert.Equal(77L, Quality(dialog).Value); // 決めた品質に戻る
        Assert.Null(dialog.Settings.SizeLimitBytes);
        await dialog.WaitForSizeAsync();
        Assert.Equal(77, dialog.SizeResult!.Encoded.UsedOptions.Get<int>(WicFormats.QualityKey));
        dialog.OnClosed();
    }

    [Fact]
    public async Task TooSmallLimit_ShowsM26()
    {
        // T16: とても小さい上限で M26（いちばん低い品質で保存する）
        var dialog = CreateDialog(CreateNoisyDocument(), sizeLimitBytes: 1 * 1024);
        await dialog.WaitForSizeAsync();

        var result = dialog.SizeResult!;
        Assert.False(result.Encoded.FitsSizeLimit);
        Assert.Equal(1L, Quality(dialog).Value); // いちばん低い品質
        Assert.Equal(Messages.M26_OverSizeLimit("1 KB", FileSizeFormatter.Format(result.FileSize)), dialog.SizeLimitWarning);
        Assert.True(dialog.HasSizeLimitWarning);

        await dialog.SaveCommand.ExecuteAsync(null); // それでも保存はできる
        Assert.Equal(result.FileSize, new FileInfo(dialog.Result!.Path).Length);
    }

    [Fact]
    public async Task SaveWithLimit_FileIsWithinLimit()
    {
        // T16: 上限 50 KB で保存したファイルが 50 KB 以下（計算したバイト列をそのまま書く）
        var dialog = CreateDialog(CreateNoisyDocument(), sizeLimitBytes: 50 * 1024);
        await dialog.WaitForSizeAsync();
        var computed = dialog.SizeResult!;

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(new FileInfo(dialog.Result!.Path).Length <= 50 * 1024);
        Assert.Same(computed.Encoded, dialog.Result.Encoded);
    }

    [Fact]
    public async Task FormatWithoutSizeTuning_HidesModeAndKeepsOptionsEditable()
    {
        var dialog = CreateDialog(CreateNoisyDocument(), sizeLimitBytes: 60 * 1024);
        Assert.True(Quality(dialog).IsReadOnly);

        dialog.SelectedFormat = Format(dialog, WicFormats.PngId);

        Assert.False(dialog.CanUseSizeLimit); // PNG はサイズを調整できない
        Assert.False(dialog.IsSizeLimitActive);
        Assert.True(dialog.UseSizeLimit);     // 品質の決め方は保つ
        Assert.False(dialog.Options[0].IsReadOnly);
        await dialog.WaitForSizeAsync();
        Assert.Null(dialog.SizeLimitWarning);

        dialog.SelectedFormat = Format(dialog, WicFormats.JpegId);
        Assert.True(Quality(dialog).IsReadOnly); // JPEG に戻ると、また上限で探す
        dialog.OnClosed();
    }

    [Fact]
    public async Task SwitchingFormatInLimitMode_KeepsUserQuality()
    {
        var dialog = CreateDialog(CreateNoisyDocument());
        Quality(dialog).SliderValue = 66;
        dialog.SizeLimitText = "40";
        dialog.UseSizeLimit = true;
        await dialog.WaitForSizeAsync(); // 入力欄には探した品質が出ている

        dialog.SelectedFormat = Format(dialog, WicFormats.PngId);
        dialog.SelectedFormat = Format(dialog, WicFormats.JpegId);
        dialog.UseQuality = true;

        Assert.Equal(66L, Quality(dialog).Value); // 覚えているのは、決めた品質
        dialog.OnClosed();
    }

    // ---------------------------------------------------------------- 上限の入力欄

    [Theory]
    [InlineData("200", 200)]
    [InlineData("２００", 200)]          // 全角
    [InlineData("0", 1)]                // 1〜1,000,000 に収める
    [InlineData("2000000", 1_000_000)]
    [InlineData("abc", 300)]            // 整数でなければ今の値のまま
    [InlineData("12.5", 300)]
    public void SizeLimitText_ParsesAndClamps(string text, long expected)
    {
        var dialog = CreateDialog(CreateNoisyDocument());

        dialog.SizeLimitText = text;

        Assert.Equal(expected, dialog.SizeLimitKB);
        Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), dialog.SizeLimitText);
        dialog.OnClosed();
    }

    [Fact]
    public async Task ChangingLimit_RecalculatesOnlyInLimitMode()
    {
        var dialog = CreateDialog(CreateNoisyDocument());
        await dialog.WaitForSizeAsync();
        var settings = dialog.Settings;

        dialog.SizeLimitText = "100"; // 品質を指定のときは、設定は変わらない
        Assert.Equal(settings, dialog.Settings);
        Assert.False(dialog.IsCalculatingSize);

        dialog.UseSizeLimit = true;
        await dialog.WaitForSizeAsync();
        dialog.SizeLimitText = "30";
        Assert.True(dialog.IsCalculatingSize); // 上限のときは計算し直す
        await dialog.WaitForSizeAsync();
        Assert.True(dialog.SizeResult!.FileSize <= 30 * 1024);
        dialog.OnClosed();
    }
}
