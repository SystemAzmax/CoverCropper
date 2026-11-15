using System.IO;
using System.Security.Cryptography;
using WebDeza.Imaging.Formats;

namespace WebDeza.Imaging.Tests;

public class FormatRegistryTests
{
    private const FormatCapabilities ReadWrite = FormatCapabilities.Decode | FormatCapabilities.Encode;

    /// <summary>標準の形式に近い組み合わせ（読み書き・読み込みだけ・書き出しだけ）</summary>
    private static (FormatRegistry Registry, FakeFormat Jpeg, FakeFormat Png, FakeFormat Gif, FakeFormat Webp) CreateRegistry()
    {
        var registry = new FormatRegistry();
        var jpeg = new FakeFormat("jpeg", ReadWrite, ".jpg", ".jpeg", ".jpe", ".jfif");
        var png = new FakeFormat("png", ReadWrite | FormatCapabilities.Alpha, ".png");
        var gif = new FakeFormat("gif", FormatCapabilities.Decode | FormatCapabilities.Alpha, ".gif");
        var webp = new FakeFormat("webp", FormatCapabilities.Encode | FormatCapabilities.Alpha, ".webp");
        registry.Register(jpeg);
        registry.Register(png);
        registry.Register(gif);
        registry.Register(webp);
        return (registry, jpeg, png, gif, webp);
    }

    // ---------------------------------------------------------------- E13: 検索

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".JPG")]
    [InlineData(".Jpg")]
    [InlineData(".JPEG")]
    [InlineData(".jfif")]
    [InlineData("jpg")]   // ドットが無くてもよい
    [InlineData("JPE")]
    public void FindByExtension_IgnoresCase(string extension)
    {
        var (registry, jpeg, _, _, _) = CreateRegistry();

        Assert.Same(jpeg, registry.FindByExtension(extension));
    }

    [Theory]
    [InlineData(".bmp")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData(".jpgx")]
    public void FindByExtension_Unknown_ReturnsNull(string extension)
    {
        var (registry, _, _, _, _) = CreateRegistry();

        Assert.Null(registry.FindByExtension(extension));
    }

    [Fact]
    public void FindById_IsCaseSensitive()
    {
        var (registry, jpeg, _, _, _) = CreateRegistry();

        Assert.Same(jpeg, registry.FindById("jpeg"));
        Assert.Null(registry.FindById("JPEG"));
        Assert.Null(registry.FindById("bmp"));
    }

    // ---------------------------------------------------------------- E13: 重複の登録

    [Fact]
    public void Register_SameId_Throws()
    {
        var (registry, _, _, _, _) = CreateRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(new FakeFormat("png", ReadWrite, ".png2")));
        Assert.Equal(4, registry.All.Count);
        Assert.Null(registry.FindByExtension(".png2")); // 途中まで登録されていない
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".JPG")] // 大文字小文字だけ違っても同じ拡張子
    [InlineData(".gif")]
    public void Register_SameExtension_Throws(string extension)
    {
        var (registry, _, _, _, _) = CreateRegistry();
        var other = new FakeFormat("other", ReadWrite, ".other", extension);

        Assert.Throws<ArgumentException>(() => registry.Register(other));
        Assert.Equal(4, registry.All.Count);
        Assert.Null(registry.FindById("other"));
        Assert.Null(registry.FindByExtension(".other")); // 先に並んでいた拡張子も登録されていない
    }

    [Fact]
    public void Register_InvalidFormat_Throws()
    {
        var registry = new FormatRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
        Assert.Throws<ArgumentException>(() => registry.Register(new FakeFormat("", ReadWrite, ".x")));
        Assert.Throws<ArgumentException>(() => registry.Register(new FakeFormat("x", ReadWrite)));                // 拡張子が無い
        Assert.Throws<ArgumentException>(() => registry.Register(new FakeFormat("x", ReadWrite, "jpg")));         // "." が無い
        Assert.Throws<ArgumentException>(() => registry.Register(new FakeFormat("x", ReadWrite, ".")));
        Assert.Throws<ArgumentException>(() => registry.Register(new FakeFormat("x", ReadWrite, ".a", ".A")));    // 自分の中で重複
        Assert.Throws<ArgumentException>(() => registry.Register(
            new FakeFormat("x", ReadWrite, ".x") { Decoder = null }));                                            // Decode なのに Decoder が無い
        Assert.Throws<ArgumentException>(() => registry.Register(
            new FakeFormat("x", ReadWrite, ".x") { Encoder = null }));                                            // Encode なのに Encoder が無い
        Assert.Empty(registry.All);
    }

    // ---------------------------------------------------------------- E13: Decodable・Encodable

    [Fact]
    public void DecodableAndEncodable_FollowCapabilitiesInRegistrationOrder()
    {
        var (registry, jpeg, png, gif, webp) = CreateRegistry();

        Assert.Equal(new IImageFormat[] { jpeg, png, gif, webp }, registry.All);
        Assert.Equal(new IImageFormat[] { jpeg, png, gif }, registry.Decodable);
        Assert.Equal(new IImageFormat[] { jpeg, png, webp }, registry.Encodable);
    }

    [Fact]
    public void EmptyRegistry_HasNoFormats()
    {
        var registry = new FormatRegistry();

        Assert.Empty(registry.All);
        Assert.Empty(registry.Decodable);
        Assert.Empty(registry.Encodable);
        Assert.False(registry.CanDecode(@"C:\a\photo.jpg"));
    }

    // ---------------------------------------------------------------- CanDecode

    [Theory]
    [InlineData(@"C:\a\photo.jpg", true)]
    [InlineData(@"C:\a\PHOTO.JPEG", true)]
    [InlineData(@"C:\a\b.c\photo.png", true)]
    [InlineData(@"C:\a\anim.gif", true)]       // 読み込みだけの形式
    [InlineData(@"C:\a\photo.webp", false)]    // 書き出しだけの形式
    [InlineData(@"C:\a\photo.bmp", false)]     // 登録されていない
    [InlineData(@"C:\a\photo", false)]         // 拡張子が無い
    [InlineData(@"C:\a\photo.jpg.txt", false)]
    public void CanDecode_UsesExtensionOfDecodableFormats(string path, bool expected)
    {
        var (registry, _, _, _, _) = CreateRegistry();

        Assert.Equal(expected, registry.CanDecode(path));
    }

    // ---------------------------------------------------------------- Decode: 元ファイルの開き方

    [Fact]
    public void Decode_ChoosesFormatByExtensionAndReturnsDecoderResult()
    {
        var (registry, jpeg, png, _, _) = CreateRegistry();
        using var temp = new TempFolder();
        var path = temp.WriteFile("photo.JPG", new byte[7]);

        var decoded = registry.Decode(path);

        Assert.Equal("jpeg", decoded.FormatId);
        Assert.Equal(7, decoded.Image.Width); // 偽のデコーダーは、ファイルの長さを幅にする
        Assert.NotNull(jpeg.FakeDecoder.LastStream);
        Assert.Null(png.FakeDecoder.LastStream);
    }

    [Fact]
    public void Decode_OpensSourceReadOnlyAndClosesAfterReading()
    {
        var (registry, jpeg, _, _, _) = CreateRegistry();
        using var temp = new TempFolder();
        var path = temp.WriteFile("photo.jpg", new byte[] { 1, 2, 3, 4, 5 });
        var hashBefore = SHA256.HashData(File.ReadAllBytes(path));
        var writeTimeBefore = File.GetLastWriteTimeUtc(path);

        bool checkedWhileOpen = false;
        jpeg.FakeDecoder.OnDecode = stream =>
        {
            // 読み取り専用で開いている
            Assert.True(stream.CanRead);
            Assert.False(stream.CanWrite);

            // 読んでいる間、ほかからの読み取りはできる（FileShare.Read）
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }

            // 読んでいる間、ほかからの書き込みはできない（元画像を書き換えさせない）
            Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
            checkedWhileOpen = true;
        };

        registry.Decode(path);

        Assert.True(checkedWhileOpen);
        // 読み終えたら閉じている
        Assert.False(jpeg.FakeDecoder.LastStream!.CanRead);
        // 中身も更新日時も変わっていない
        Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(path));
        // ロックが残っていなければ、リネームも削除もできる
        var renamed = temp.Combine("renamed.jpg");
        File.Move(path, renamed);
        File.Delete(renamed);
        Assert.False(File.Exists(renamed));
    }

    [Fact]
    public void Decode_DecoderThrows_StillClosesSource()
    {
        var (registry, jpeg, _, _, _) = CreateRegistry();
        using var temp = new TempFolder();
        var path = temp.WriteFile("broken.jpg", new byte[3]);
        jpeg.FakeDecoder.OnDecode = _ => throw new InvalidDataException("壊れたファイル");

        // デコーダーのそのほかの例外は、そのまま伝わる（アプリは M02 にする）
        Assert.Throws<InvalidDataException>(() => registry.Decode(path));

        Assert.False(jpeg.FakeDecoder.LastStream!.CanRead);
        File.Delete(path); // ロックが残っていない
    }

    // ---------------------------------------------------------------- Decode: 読めないとき

    [Theory]
    [InlineData("photo.bmp")]   // 登録されていない拡張子
    [InlineData("photo")]       // 拡張子が無い
    [InlineData("photo.webp")]  // 書き出しだけの形式
    public void Decode_NotDecodable_ThrowsUnsupportedWithoutOpeningFile(string fileName)
    {
        var (registry, _, _, _, webp) = CreateRegistry();
        using var temp = new TempFolder();
        // ファイルが無くても、ファイルを開く前に判定する（FileNotFoundException にならない）
        var path = temp.Combine(fileName);

        var ex = Assert.Throws<UnsupportedImageFormatException>(() => registry.Decode(path));

        Assert.IsAssignableFrom<NotSupportedException>(ex); // CoverCropper の M01 の判定がそのまま使える
        Assert.Equal(UnsupportedImageFormatException.DefaultMessage, ex.Message);
        Assert.Null(ex.Requirement);
        Assert.Equal(fileName == "photo.webp" ? "webp" : null, ex.FormatId);
        Assert.Throws<UnsupportedImageFormatException>(() => registry.DecodeThumbnail(path, 160));
        Assert.Null(webp.Decoder);
    }

    [Fact]
    public void Decode_DecoderReportsUnsupported_AddsRequirementOfFormat()
    {
        // OS の拡張機能が無くて読めなかったとき（HEIF など）
        var registry = new FormatRegistry();
        var heif = new FakeFormat("heif", FormatCapabilities.Decode, ".heic", ".heif")
        {
            Requirement = "Microsoft Store の「HEIF 画像拡張機能」",
        };
        heif.FakeDecoder.OnDecode = _ => throw new UnsupportedImageFormatException("コーデックがありません。");
        registry.Register(heif);
        using var temp = new TempFolder();
        var path = temp.WriteFile("photo.heic", new byte[4]);

        var ex = Assert.Throws<UnsupportedImageFormatException>(() => registry.Decode(path));

        Assert.Equal("heif", ex.FormatId);
        Assert.Equal("Microsoft Store の「HEIF 画像拡張機能」", ex.Requirement);
        Assert.Equal("コーデックがありません。", ex.Message);
        Assert.IsType<UnsupportedImageFormatException>(ex.InnerException);
        File.Delete(path); // ロックが残っていない
    }

    [Fact]
    public void Decode_DecoderReportsUnsupportedWithRequirement_KeepsIt()
    {
        var (registry, jpeg, _, _, _) = CreateRegistry();
        var original = new UnsupportedImageFormatException("読めません。", "jpeg", "特別なコーデック");
        jpeg.FakeDecoder.OnDecode = _ => throw original;
        using var temp = new TempFolder();
        var path = temp.WriteFile("photo.jpg", new byte[4]);

        var ex = Assert.Throws<UnsupportedImageFormatException>(() => registry.Decode(path));

        Assert.Same(original, ex);
    }

    [Fact]
    public void Decode_MissingFile_ThrowsFileNotFound()
    {
        var (registry, _, _, _, _) = CreateRegistry();
        using var temp = new TempFolder();

        Assert.Throws<FileNotFoundException>(() => registry.Decode(temp.Combine("none.jpg")));
    }

    [Fact]
    public void Decode_CancelledToken_ThrowsBeforeOpening()
    {
        var (registry, jpeg, _, _, _) = CreateRegistry();
        using var temp = new TempFolder();
        var path = temp.WriteFile("photo.jpg", new byte[4]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => registry.Decode(path, cts.Token));
        Assert.Null(jpeg.FakeDecoder.LastStream);
    }

    // ---------------------------------------------------------------- DecodeThumbnail

    [Fact]
    public void DecodeThumbnail_PassesMaxSizeAndClosesSource()
    {
        var (registry, _, png, _, _) = CreateRegistry();
        using var temp = new TempFolder();
        var path = temp.WriteFile("icon.png", new byte[9]);

        var thumbnail = registry.DecodeThumbnail(path, 160);

        Assert.Equal(9, thumbnail.Width);
        Assert.Equal(160, png.FakeDecoder.LastMaxSize);
        Assert.False(png.FakeDecoder.LastStream!.CanWrite);
        Assert.False(png.FakeDecoder.LastStream.CanRead); // 閉じている
        File.Delete(path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DecodeThumbnail_NonPositiveMaxSize_Throws(int maxSize)
    {
        var (registry, _, _, _, _) = CreateRegistry();

        Assert.Throws<ArgumentOutOfRangeException>(() => registry.DecodeThumbnail(@"C:\a\photo.jpg", maxSize));
    }
}
