using System.IO;
using WebDeza.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Services;

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
