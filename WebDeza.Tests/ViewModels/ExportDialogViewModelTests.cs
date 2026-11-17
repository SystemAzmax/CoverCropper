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

/// <summary>保存のダイアログ: 出力先と保存、形式とオプション（設計書 F-08・F-10、V23。実装計画書 10-2・10-3）</summary>
public sealed class ExportDialogViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 30, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FormatRegistry _formats = new();
    private readonly string _source;
    private readonly string _output;

    public ExportDialogViewModelTests()
    {
        _formats.AddWicFormats();
        _source = _temp.Combine("photo.png");
        File.WriteAllBytes(_source, [1, 2, 3]);
        _output = _temp.Combine("out");
        Directory.CreateDirectory(_output);
    }

    public void Dispose() => _temp.Dispose();

    private string LogFile => _temp.Combine("logs", "app.log");

    private static ExportSettings Jpeg => new(WicFormats.JpegId, new Dictionary<string, ParameterValues>(), null, new RgbColor(255, 255, 255));

    private DocumentViewModel CreateDocument(int width = 40, int height = 30)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 4 == 3 ? 255 : i * 5 % 251);
        var image = ImageBuffer.Create(width, height, pixels);
        return new DocumentViewModel(_source, image, DisplayImage.Create(image), 3, (_, _) => null, _dialogs, new AppLog(LogFile));
    }

    private ExportDialogViewModel CreateDialog(DocumentViewModel document, string? outputFolder, ExportSettings? settings = null,
        OutputPathFactory? createPath = null)
        => new(document, outputFolder, settings ?? Jpeg, new ExportService(_formats, new ManualTimeProvider(Now), createPath),
            _dialogs, new AppLog(LogFile));

    // ---------------------------------------------------------------- 表示

    [Fact]
    public void Initially_ShowsFormatSizeFolderAndFileName()
    {
        var dialog = CreateDialog(CreateDocument(), _output);

        Assert.Equal("保存: photo.png", dialog.Title);
        Assert.Equal("JPEG", dialog.SelectedFormat!.DisplayName);
        Assert.Equal("40 × 30 px", dialog.SizeText); // 今の画像の大きさのまま保存する
        Assert.Equal(_output, dialog.OutputFolder);
        Assert.Equal(_output, dialog.OutputFolderText);
        Assert.Equal("photo_20261117_103000.jpg", dialog.FileName);
        Assert.True(dialog.SaveCommand.CanExecute(null));
        Assert.True(dialog.OpenFolderCommand.CanExecute(null));
        Assert.True(dialog.IsIdle);
        Assert.Null(dialog.Result);
    }

    [Fact]
    public void FileName_AvoidsExistingFiles_AndWorksWithoutFolder()
    {
        File.WriteAllBytes(Path.Combine(_output, "photo_20261117_103000.jpg"), [0]);

        Assert.Equal("photo_20261117_103000_2.jpg", CreateDialog(CreateDocument(), _output).FileName);

        var unset = CreateDialog(CreateDocument(), null);
        Assert.Null(unset.OutputFolder);
        Assert.Equal("（未設定）", unset.OutputFolderText);
        Assert.Equal("photo_20261117_103000.jpg", unset.FileName); // 出力先が無くても、名前は出す
        Assert.False(unset.OpenFolderCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 形式とオプション（10-3）

    private static NumericParameterViewModel Quality(ExportDialogViewModel dialog)
        => (NumericParameterViewModel)dialog.Options.Single(o => o.Key == WicFormats.QualityKey);

    private static IImageFormat Format(ExportDialogViewModel dialog, string id) => dialog.Formats.Single(f => f.Id == id);

    [Fact]
    public void Formats_AreEncodableOnes_InRegistrationOrder()
    {
        var dialog = CreateDialog(CreateDocument(), _output);

        Assert.Equal([WicFormats.JpegId, WicFormats.PngId, WicFormats.BmpId, WicFormats.TiffId], dialog.Formats.Select(f => f.Id));
        Assert.Equal(["品質"], dialog.Options.Select(o => o.DisplayName)); // JPEG のオプション
        Assert.Equal(85L, Quality(dialog).Value); // 既定値
        Assert.False(dialog.HasNoOptions);
    }

    [Fact]
    public void SwitchingFormat_KeepsEachFormatsOptions()
    {
        // V23: 形式を JPEG → PNG → JPEG → JPEG の品質が保たれている
        var dialog = CreateDialog(CreateDocument(), _output);
        Quality(dialog).SliderValue = 60;

        dialog.SelectedFormat = Format(dialog, WicFormats.PngId);
        Assert.Equal(["インターレース"], dialog.Options.Select(o => o.DisplayName));
        Assert.Equal("photo_20261117_103000.png", dialog.FileName); // 拡張子が形式に合わせて変わる
        ((BooleanParameterViewModel)dialog.Options[0]).IsChecked = true;

        dialog.SelectedFormat = Format(dialog, WicFormats.BmpId);
        Assert.Empty(dialog.Options);
        Assert.True(dialog.HasNoOptions);

        dialog.SelectedFormat = Format(dialog, WicFormats.JpegId);
        Assert.Equal(60L, Quality(dialog).Value);
        Assert.Equal("photo_20261117_103000.jpg", dialog.FileName);

        dialog.SelectedFormat = Format(dialog, WicFormats.PngId);
        Assert.Equal(true, dialog.Options[0].Value);

        // 保存の設定には、今の形式と、ほかの形式の覚えた値が入る
        var settings = dialog.Settings;
        Assert.Equal(WicFormats.PngId, settings.FormatId);
        Assert.Equal(60, settings.GetOptions(WicFormats.JpegId).Get<int>(WicFormats.QualityKey));
        Assert.True(settings.GetOptions(WicFormats.PngId).Get<bool>(WicFormats.InterlaceKey));
    }

    [Fact]
    public void Settings_ReflectOptionChanges_AndKeepLimitAndBackground()
    {
        var initial = new ExportSettings(WicFormats.TiffId,
            new Dictionary<string, ParameterValues> { [WicFormats.JpegId] = ParameterValues.Empty.With(WicFormats.QualityKey, 40L) },
            300 * 1024, new RgbColor(1, 2, 3));
        var dialog = CreateDialog(CreateDocument(), _output, initial);
        var changed = new List<string?>();
        dialog.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal(WicFormats.TiffId, dialog.SelectedFormat!.Id); // 前回の形式で開く
        var compression = (ChoiceParameterViewModel)dialog.Options[0];
        Assert.Equal(WicFormats.CompressionLzw, compression.Value);
        compression.SelectedChoice = compression.Choices.Single(c => c.Value == WicFormats.CompressionZip);

        Assert.Contains(nameof(ExportDialogViewModel.Settings), changed); // 値を変えると、設定が変わったことを知らせる
        var settings = dialog.Settings;
        Assert.Equal(WicFormats.CompressionZip, settings.GetOptions(WicFormats.TiffId).Get<string>(WicFormats.CompressionKey));
        Assert.Equal(40, settings.GetOptions(WicFormats.JpegId).Get<int>(WicFormats.QualityKey)); // 開いていない形式の値も保つ
        Assert.Equal(300 * 1024, settings.SizeLimitBytes);
        Assert.Equal(new RgbColor(1, 2, 3), settings.Background);

        dialog.SelectedFormat = Format(dialog, WicFormats.JpegId);
        Assert.Equal(40L, Quality(dialog).Value); // 覚えていた値で入力欄を作る
    }

    [Fact]
    public void UnknownFormatInSettings_FallsBackToFirst()
    {
        var dialog = CreateDialog(CreateDocument(), _output,
            new ExportSettings("unknown", new Dictionary<string, ParameterValues>(), null, new RgbColor(255, 255, 255)));

        Assert.Equal(WicFormats.JpegId, dialog.SelectedFormat!.Id);
        Assert.Equal(WicFormats.JpegId, dialog.Settings.FormatId);
    }

    [Fact]
    public void SelectedFormat_IgnoresNullAndUnknown()
    {
        var dialog = CreateDialog(CreateDocument(), _output);
        var jpeg = dialog.SelectedFormat;

        dialog.SelectedFormat = null;
        dialog.SelectedFormat = _formats.FindById(WicFormats.GifId); // 読めるが保存できない
        Assert.Same(jpeg, dialog.SelectedFormat);
    }

    [Theory]
    [InlineData(WicFormats.PngId, ".png", true)]   // T06: PNG で保存すると透過が残る
    [InlineData(WicFormats.TiffId, ".tif", true)]
    [InlineData(WicFormats.JpegId, ".jpg", false)] // JPEG では背景色で塗られる
    [InlineData(WicFormats.BmpId, ".bmp", false)]
    public async Task Save_WithSelectedFormat(string formatId, string extension, bool keepsAlpha)
    {
        var pixels = new byte[40 * 30 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 1] = 100; pixels[i + 2] = 50; pixels[i + 3] = 255; }
        pixels[3] = 0; // 左上だけ透明
        var image = ImageBuffer.Create(40, 30, pixels);
        var document = new DocumentViewModel(_source, image, DisplayImage.Create(image), 3, (_, _) => null, _dialogs, new AppLog(LogFile));
        var dialog = CreateDialog(document, _output);
        dialog.SelectedFormat = Format(dialog, formatId);

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.EndsWith(extension, dialog.Result!.Path);
        var decoded = _formats.Decode(dialog.Result.Path).Image;
        Assert.Equal(keepsAlpha ? 0 : 255, decoded.Pixels[3]);
        if (formatId == WicFormats.BmpId) Assert.Equal(new byte[] { 255, 255, 255 }, decoded.Pixels[0..3]); // 背景色（白。JPEG は非可逆なので見ない）
    }

    [Fact]
    public async Task Save_UsesOptionValues()
    {
        var dialog = CreateDialog(CreateDocument(200, 150), _output);
        Quality(dialog).SliderValue = 10;
        await dialog.SaveCommand.ExecuteAsync(null);
        long low = dialog.Result!.FileSize;

        var high = CreateDialog(CreateDocument(200, 150), _output);
        Quality(high).SliderValue = 100;
        await high.SaveCommand.ExecuteAsync(null);

        Assert.Equal(10, dialog.Result.Encoded.UsedOptions.Get<int>(WicFormats.QualityKey));
        Assert.True(high.Result!.FileSize > low); // 品質が高いほど大きい
    }

    // ---------------------------------------------------------------- 出力先

    [Fact]
    public void ChangeFolder_SelectsFolder_OrKeepsWhenCancelled()
    {
        var other = _temp.Combine("other");
        Directory.CreateDirectory(other);
        File.WriteAllBytes(Path.Combine(other, "photo_20261117_103000.jpg"), [0]);
        var dialog = CreateDialog(CreateDocument(), _output);

        _dialogs.FolderToSelect = null;
        dialog.ChangeFolderCommand.Execute(null);
        Assert.Equal(_output, dialog.OutputFolder);
        Assert.Equal([_output], _dialogs.FolderDialogRequests); // 今の出力先から選ばせる

        _dialogs.FolderToSelect = other;
        dialog.ChangeFolderCommand.Execute(null);
        Assert.Equal(other, dialog.OutputFolder);
        Assert.Equal("photo_20261117_103000_2.jpg", dialog.FileName); // 新しい出力先で名前を決め直す
    }

    [Fact]
    public void OpenFolder_OpensInExplorer_OrShowsM06()
    {
        CreateDialog(CreateDocument(), _output).OpenFolderCommand.Execute(null);
        Assert.Equal([_output], _dialogs.OpenedFolders);

        var missing = _temp.Combine("missing");
        CreateDialog(CreateDocument(), missing).OpenFolderCommand.Execute(null);
        Assert.Equal([Messages.M06_OutputFolderNotFound(missing)], _dialogs.Errors);
        Assert.Single(_dialogs.OpenedFolders);
    }

    // ---------------------------------------------------------------- 保存

    [Fact]
    public async Task Save_WritesFile_MarksExported_AndRequestsClose()
    {
        var document = CreateDocument();
        await document.ApplyEditAsync("編集", (image, _) => ImageBuffer.Create(image.Width, image.Height, (byte[])image.Pixels.Clone()),
            changedRegion: null, newSelection: null, resetView: false);
        Assert.True(document.HasUnexportedEdits);
        var dialog = CreateDialog(document, _output);
        ExportResult? saved = null;
        int closeRequested = 0;
        dialog.Saved += (_, r) => saved = r;
        dialog.CloseRequested += (_, _) => closeRequested++;
        var busy = new List<bool>();
        dialog.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ExportDialogViewModel.IsSaving)) busy.Add(dialog.IsSaving); };

        await dialog.SaveCommand.ExecuteAsync(null);

        var path = Path.Combine(_output, "photo_20261117_103000.jpg");
        Assert.True(File.Exists(path));
        Assert.Same(dialog.Result, saved);
        Assert.Equal(path, dialog.Result!.Path);
        Assert.Equal(new FileInfo(path).Length, dialog.Result.FileSize);
        Assert.Equal(1, closeRequested);
        Assert.Equal([true, false], busy);
        Assert.False(document.HasUnexportedEdits); // 保存すると * が消える
        Assert.Equal("photo.png", document.Title);
        Assert.Equal(1, document.ExportCount);
        Assert.True(document.CanUndo); // 画像と履歴はそのまま
        Assert.Equal([1, 2, 3], File.ReadAllBytes(_source)); // 元画像は変わらない
        var decoded = _formats.Decode(path).Image;
        Assert.Equal((40, 30), (decoded.Width, decoded.Height));
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task Save_SavesCurrentImage_NotFilterPreview()
    {
        // 保存するのは、確定した今の画像（フィルターのプレビューは含めない。設計書 F-10）
        var document = CreateDocument();
        var preview = document.BaseDisplay.WithImage(ImageBuffer.Create(40, 30, Enumerable.Repeat((byte)255, 40 * 30 * 4).ToArray()));
        document.SetPreview(preview);
        var dialog = CreateDialog(document, _output, new ExportSettings(WicFormats.PngId, new Dictionary<string, ParameterValues>(), null,
            new RgbColor(255, 255, 255)));

        await dialog.SaveCommand.ExecuteAsync(null);

        var decoded = _formats.Decode(dialog.Result!.Path).Image;
        Assert.Equal(document.Image.Pixels, decoded.Pixels);
    }

    [Fact]
    public async Task Save_FolderNotSet_AsksM05_ThenUsesSelectedFolder()
    {
        var dialog = CreateDialog(CreateDocument(), null);
        _dialogs.FolderToSelect = _output;

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal([Messages.M05_OutputFolderNotSet], _dialogs.OkCancelConfirmations);
        Assert.Equal(_output, dialog.OutputFolder); // 選んだフォルダを出力先にして保存を続ける
        Assert.NotNull(dialog.Result);
        Assert.True(File.Exists(dialog.Result.Path));
    }

    [Theory]
    [InlineData(false, null)]  // M05 で［キャンセル］
    [InlineData(true, null)]   // フォルダを選ばなかった
    public async Task Save_FolderNotSet_Cancelled_DoesNothing(bool okCancelAnswer, string? folder)
    {
        var dialog = CreateDialog(CreateDocument(), null);
        _dialogs.OkCancelAnswer = okCancelAnswer;
        _dialogs.FolderToSelect = folder;
        int closeRequested = 0;
        dialog.CloseRequested += (_, _) => closeRequested++;

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Null(dialog.Result);
        Assert.Null(dialog.OutputFolder);
        Assert.Equal(0, closeRequested); // ダイアログは閉じない
        Assert.Empty(Directory.GetFiles(_output));
    }

    [Fact]
    public async Task Save_FolderNotFound_ShowsM06_AndKeepsDialog()
    {
        var missing = _temp.Combine("missing");
        var document = CreateDocument();
        await document.ApplyEditAsync("編集", (image, _) => image, changedRegion: null, newSelection: null, resetView: false);
        var dialog = CreateDialog(document, missing);
        int closeRequested = 0;
        dialog.CloseRequested += (_, _) => closeRequested++;

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal([Messages.M06_OutputFolderNotFound(missing)], _dialogs.Errors);
        Assert.Null(dialog.Result);
        Assert.Equal(0, closeRequested);
        Assert.True(document.HasUnexportedEdits);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task Save_Failure_ShowsM07_AndKeepsDialog()
    {
        // 保存先が元画像と同じになる（ファイル名の決め方を差し替え）→ 書かずに M07
        var document = CreateDocument();
        await document.ApplyEditAsync("編集", (image, _) => image, changedRegion: null, newSelection: null, resetView: false);
        var dialog = CreateDialog(document, _output, createPath: (_, source, _, _) => source);
        int closeRequested = 0;
        dialog.CloseRequested += (_, _) => closeRequested++;

        await dialog.SaveCommand.ExecuteAsync(null);

        var error = Assert.Single(_dialogs.Errors);
        Assert.StartsWith("保存に失敗しました。（", error);
        Assert.Null(dialog.Result);
        Assert.Equal(0, closeRequested);
        Assert.False(dialog.IsSaving);
        Assert.True(dialog.SaveCommand.CanExecute(null)); // やり直せる
        Assert.True(document.HasUnexportedEdits);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(_source));
        Assert.Contains("Export failed", File.ReadAllText(LogFile));
    }

    [Fact]
    public void SavedMessage_IsM04()
    {
        var result = new ExportResult(@"D:\out\photo_20261117_103000.jpg",
            new EncodedImage(new byte[186_778], WicFormats.JpegId, ParameterValues.Empty, true));

        Assert.Equal("保存しました: photo_20261117_103000.jpg（182.4 KB）", ExportDialogViewModel.SavedMessage(result));
    }
}
