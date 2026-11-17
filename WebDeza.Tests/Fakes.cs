using System.IO;
using WebDeza.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Services;
using WebDeza.ViewModels;

namespace WebDeza.Tests;

/// <summary>テスト用の形式。読み込みの結果（または投げる例外）を決めておける</summary>
internal sealed class FakeFormat : IImageFormat, IImageDecoder
{
    public FakeFormat(string id, params string[] extensions)
    {
        Id = id;
        Extensions = extensions;
    }

    public string Id { get; }
    public string DisplayName { get; init; } = "Fake";
    public IReadOnlyList<string> Extensions { get; }
    public FormatCapabilities Capabilities => FormatCapabilities.Decode | FormatCapabilities.Alpha;
    public string? Requirement { get; init; }
    public IImageDecoder? Decoder => this;
    public IImageEncoder? Encoder => null;

    /// <summary>読み込んだときに返す画像</summary>
    public ImageBuffer Image { get; set; } = ImageBuffer.Create(30, 20, new byte[30 * 20 * 4]);

    /// <summary>読み込んだときに投げる例外（null なら Image を返す）</summary>
    public Func<Exception>? Throw { get; set; }

    /// <summary>読み込みの最初に呼ぶ処理（読み込み中の状態を確かめるために、止めておくのに使う）</summary>
    public Action? OnDecode { get; set; }

    /// <summary>読み込みを呼ばれた回数</summary>
    public int DecodeCount { get; private set; }

    public DecodedImage Decode(Stream stream, CancellationToken cancellationToken)
    {
        DecodeCount++;
        OnDecode?.Invoke();
        if (Throw is not null) throw Throw();
        return new DecodedImage(Image, Id, new ImageMetadata(1, 1));
    }

    public ImageBuffer DecodeThumbnail(Stream stream, int maxSize, CancellationToken cancellationToken)
        => Decode(stream, cancellationToken).Image;
}

/// <summary>テスト用のダイアログ。出したエラーを覚え、「開く」で返すパスを決めておける</summary>
internal sealed class FakeDialogs : IDialogService
{
    public List<string> Errors { get; } = new();

    /// <summary>エラーを出すときに呼ぶ処理（出したときの状態を確かめるのに使う）</summary>
    public Action<string>? OnShowError { get; set; }

    /// <summary>「開く」で返すパス（null ならやめた）</summary>
    public IReadOnlyList<string>? PathsToOpen { get; set; }

    /// <summary>「開く」で 1 件だけを返す（null ならやめた）</summary>
    public string? PathToOpen
    {
        get => PathsToOpen is [var path] ? path : null;
        set => PathsToOpen = value is null ? null : [value];
    }

    /// <summary>「開く」に渡された（最初のフォルダ, 種類の一覧）</summary>
    public List<(string? InitialDirectory, string Filter)> OpenRequests { get; } = new();

    /// <summary>画像サイズのダイアログで行うこと（入力欄を変えて、OK なら true を返す）。null ならキャンセル</summary>
    public Func<ImageSizeDialogViewModel, bool>? OnImageSizeDialog { get; set; }

    /// <summary>出した画像サイズのダイアログ</summary>
    public List<ImageSizeDialogViewModel> ImageSizeDialogs { get; } = new();

    public bool ShowImageSizeDialog(ImageSizeDialogViewModel viewModel)
    {
        ImageSizeDialogs.Add(viewModel);
        return OnImageSizeDialog?.Invoke(viewModel) ?? false;
    }

    /// <summary>確認したときの文（順に）</summary>
    public List<string> Confirmations { get; } = new();

    /// <summary>確認への答え（true なら［はい］）</summary>
    public bool ConfirmAnswer { get; set; } = true;

    public bool Confirm(string message)
    {
        Confirmations.Add(message);
        return ConfirmAnswer;
    }

    /// <summary>［OK］［キャンセル］の確認をしたときの文（順に）</summary>
    public List<string> OkCancelConfirmations { get; } = new();

    /// <summary>［OK］［キャンセル］の確認への答え（true なら［OK］）</summary>
    public bool OkCancelAnswer { get; set; } = true;

    public bool ConfirmOkCancel(string message)
    {
        OkCancelConfirmations.Add(message);
        return OkCancelAnswer;
    }

    /// <summary>フォルダのダイアログで返すフォルダ（null ならやめた）</summary>
    public string? FolderToSelect { get; set; }

    /// <summary>フォルダのダイアログに渡された最初のフォルダ（順に）</summary>
    public List<string?> FolderDialogRequests { get; } = new();

    public string? ShowFolderDialog(string? initialDirectory)
    {
        FolderDialogRequests.Add(initialDirectory);
        return FolderToSelect;
    }

    /// <summary>エクスプローラーで開いたフォルダ（順に）</summary>
    public List<string> OpenedFolders { get; } = new();

    public void OpenFolder(string folder) => OpenedFolders.Add(folder);

    /// <summary>出した保存のダイアログ（中で保存するときは、テストが SaveCommand を実行する）</summary>
    public List<ExportDialogViewModel> ExportDialogs { get; } = new();

    /// <summary>保存のダイアログを出したときに行うこと（閉じるときに書き戻す値を入れるなど）</summary>
    public Action<ExportDialogViewModel>? OnExportDialogShown { get; set; }

    public bool ShowExportDialog(ExportDialogViewModel viewModel)
    {
        ExportDialogs.Add(viewModel);
        OnExportDialogShown?.Invoke(viewModel);
        return false;
    }

    public void ShowError(string message)
    {
        OnShowError?.Invoke(message);
        Errors.Add(message);
    }

    public IReadOnlyList<string>? ShowOpenImageDialog(string? initialDirectory, string filter)
    {
        OpenRequests.Add((initialDirectory, filter));
        return PathsToOpen;
    }
}

/// <summary>
/// テスト用のフィルター。パラメーターは「量」（0〜255 の整数。既定は 0 で、0 のときは何もしない）だけで、
/// かけると、渡された画像のすべてのバイトを量の値にする。呼ばれたときの状況を覚える
/// </summary>
internal sealed class FakeFilter : WebDeza.Imaging.Filters.IImageFilter
{
    public const string AmountKey = "amount";

    private readonly List<(int Width, int Height, int Amount, double PixelScale)> _calls = new();

    public FakeFilter(string id = "fake") => Id = id;

    public string Id { get; }
    public string DisplayName => "テスト用 " + Id;

    public IReadOnlyList<ParameterDescriptor> Parameters { get; } =
        [new ParameterDescriptor(AmountKey, "量", ParameterKind.Integer, 0, Min: 0, Max: 255)];

    /// <summary>かける前に呼ぶ処理（計算中の状態を確かめるために、止めておくのに使う）</summary>
    public Action? OnApply { get; set; }

    /// <summary>かけたときに投げる例外（null なら投げない）</summary>
    public Func<Exception>? Throw { get; set; }

    /// <summary>かけた回数ぶんの（渡された画像の幅, 高さ, 量, PixelScale）</summary>
    public IReadOnlyList<(int Width, int Height, int Amount, double PixelScale)> Calls
    {
        get { lock (_calls) return _calls.ToList(); }
    }

    public bool IsIdentity(ParameterValues values) => values.Get<int>(AmountKey) == 0;

    public ImageBuffer Apply(ImageBuffer source, ParameterValues values, WebDeza.Imaging.Filters.FilterContext context)
    {
        int amount = values.Get<int>(AmountKey);
        lock (_calls) _calls.Add((source.Width, source.Height, amount, context.PixelScale));
        OnApply?.Invoke();
        if (Throw is not null) throw Throw();
        var pixels = new byte[source.Pixels.Length];
        Array.Fill(pixels, (byte)amount);
        return ImageBuffer.Create(source.Width, source.Height, pixels);
    }
}

/// <summary>
/// テスト用の保存できる形式。オプションは「品質」（1〜100、既定 85。SizeTuning）。
/// エンコードすると「FAKE」と画素数・品質を書いたバイト列を作り、渡された画像とオプションを覚える
/// </summary>
internal sealed class FakeEncoderFormat : IImageFormat, IImageEncoder
{
    public const string QualityKey = "quality";

    public FakeEncoderFormat(string id, bool alpha, params string[] extensions)
    {
        Id = id;
        Extensions = extensions;
        Capabilities = FormatCapabilities.Encode | (alpha ? FormatCapabilities.Alpha : FormatCapabilities.None);
    }

    public string Id { get; }
    public string DisplayName => "Fake " + Id;
    public IReadOnlyList<string> Extensions { get; }
    public FormatCapabilities Capabilities { get; }
    public string? Requirement => null;
    public IImageDecoder? Decoder => null;
    public IImageEncoder? Encoder => this;

    public IReadOnlyList<ParameterDescriptor> Options { get; } =
        [new ParameterDescriptor(QualityKey, "品質", ParameterKind.Integer, 85, Min: 1, Max: 100)];

    public SizeTuning? SizeTuning { get; } = new(QualityKey);

    /// <summary>エンコードを呼ばれたときの（画像, オプション）</summary>
    public List<(ImageBuffer Image, ParameterValues Options)> Calls { get; } = new();

    /// <summary>エンコードの最初に呼ぶ処理（計算中の状態を確かめるために、止めておくのに使う）</summary>
    public Action? OnEncode { get; set; }

    /// <summary>エンコードしたときに投げる例外（null なら投げない）</summary>
    public Func<Exception>? Throw { get; set; }

    public void Encode(ImageBuffer image, ParameterValues options, Stream output, CancellationToken cancellationToken)
    {
        lock (Calls) Calls.Add((image, options));
        OnEncode?.Invoke();
        if (Throw is not null) throw Throw();
        var text = $"FAKE {image.Width}x{image.Height} q={options.Get<int>(QualityKey)}";
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        output.Write(bytes);
    }
}
