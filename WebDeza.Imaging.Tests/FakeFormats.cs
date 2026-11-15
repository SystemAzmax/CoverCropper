using System.IO;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

/// <summary>テスト用の偽の形式。中身は自由に差し替えられる</summary>
internal sealed class FakeFormat : IImageFormat
{
    public FakeFormat(string id, FormatCapabilities capabilities, params string[] extensions)
    {
        Id = id;
        DisplayName = id.ToUpperInvariant();
        Capabilities = capabilities;
        Extensions = extensions;
        Decoder = capabilities.HasFlag(FormatCapabilities.Decode) ? new FakeDecoder(id) : null;
        Encoder = capabilities.HasFlag(FormatCapabilities.Encode) ? new FakeEncoder() : null;
    }

    public string Id { get; }
    public string DisplayName { get; init; }
    public IReadOnlyList<string> Extensions { get; init; }
    public FormatCapabilities Capabilities { get; }
    public string? Requirement { get; init; }
    public IImageDecoder? Decoder { get; init; }
    public IImageEncoder? Encoder { get; init; }

    public FakeDecoder FakeDecoder => (FakeDecoder)Decoder!;
}

/// <summary>
/// テスト用の偽のデコーダー。呼ばれたときの stream を覚えておき、OnDecode で好きな確認や例外を差し込める。
/// 読んだ結果は、ファイルの長さを幅にした 1 行の画像
/// </summary>
internal sealed class FakeDecoder(string formatId) : IImageDecoder
{
    public Action<Stream>? OnDecode { get; set; }
    public Stream? LastStream { get; private set; }
    public int? LastMaxSize { get; private set; }

    public DecodedImage Decode(Stream stream, CancellationToken cancellationToken)
    {
        LastStream = stream;
        OnDecode?.Invoke(stream);
        return new DecodedImage(ReadAsImage(stream), formatId, new ImageMetadata(1, 1));
    }

    public ImageBuffer DecodeThumbnail(Stream stream, int maxSize, CancellationToken cancellationToken)
    {
        LastStream = stream;
        LastMaxSize = maxSize;
        OnDecode?.Invoke(stream);
        return ReadAsImage(stream);
    }

    private static ImageBuffer ReadAsImage(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        int width = Math.Max(1, (int)memory.Length);
        var pixels = new byte[width * 4];
        Array.Fill(pixels, (byte)255);
        return ImageBuffer.Create(width, 1, pixels);
    }
}

/// <summary>
/// テスト用の偽のエンコーダー。既定では品質（1〜100、既定 85）を持ち、品質 × 10 バイトを書く。
/// 呼ばれたときのオプションと画像を記録する
/// </summary>
internal sealed class FakeEncoder : IImageEncoder
{
    public IReadOnlyList<ParameterDescriptor> Options { get; init; } = new[]
    {
        new ParameterDescriptor("quality", "品質", ParameterKind.Integer, 85, Min: 1, Max: 100),
    };

    public SizeTuning? SizeTuning { get; init; } = new("quality");

    /// <summary>オプションから書くバイト数を決める（既定は品質 × 10。品質が無ければ 100）</summary>
    public Func<ParameterValues, int> SizeOf { get; init; }
        = options => options.ContainsKey("quality") ? options.Get<int>("quality") * 10 : 100;

    public List<ParameterValues> Calls { get; } = new();
    public ImageBuffer? LastImage { get; private set; }

    public void Encode(ImageBuffer image, ParameterValues options, Stream output, CancellationToken cancellationToken)
    {
        Calls.Add(options);
        LastImage = image;
        output.Write(new byte[SizeOf(options)]);
    }
}

/// <summary>テストごとの一時フォルダ</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WebDezaImagingTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>中身を書いたファイルを作り、そのパスを返す</summary>
    public string WriteFile(string name, byte[] contents)
    {
        var path = Combine(name);
        File.WriteAllBytes(path, contents);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
