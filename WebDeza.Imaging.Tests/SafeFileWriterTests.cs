using System.IO;
using WebDeza.Imaging.IO;

namespace WebDeza.Imaging.Tests;

public class SafeFileWriterTests
{
    private static readonly byte[] Contents = { 0xFF, 0xD8, 1, 2, 3, 4, 0xFF, 0xD9 };

    /// <summary>2 つの書き方（バイト列・書く処理）</summary>
    public static TheoryData<string> Overloads => new() { "bytes", "action" };

    private static void Write(string overload, string path, byte[] contents, params string[] protectedPaths)
    {
        if (overload == "bytes")
            SafeFileWriter.Write(path, contents, protectedPaths);
        else
            SafeFileWriter.Write(path, stream => stream.Write(contents), protectedPaths);
    }

    // ---------------------------------------------------------------- 書き込み

    [Theory]
    [MemberData(nameof(Overloads))]
    public void Write_WritesContentsWithoutLeavingTempFiles(string overload)
    {
        using var temp = new TempFolder();
        var path = temp.Combine("photo_20261115_120000.jpg");

        Write(overload, path, Contents);

        Assert.Equal(Contents, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(temp.Path)); // 一時ファイルが残っていない
    }

    [Fact]
    public void Write_EmptyContents_CreatesEmptyFile()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("empty.bin");

        SafeFileWriter.Write(path, ReadOnlySpan<byte>.Empty, Array.Empty<string>());

        Assert.Equal(0, new FileInfo(path).Length);
    }

    [Fact]
    public void Write_WritesToTempFileInSameFolderThenRenames()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("photo.jpg");
        string[]? filesWhileWriting = null;

        SafeFileWriter.Write(path, stream =>
        {
            stream.Write(Contents);
            filesWhileWriting = Directory.GetFiles(temp.Path);
        }, Array.Empty<string>());

        // 書いている間は、同じフォルダの一時ファイル（.{名前}_{GUID}.tmp）だけがあり、書き出し先はまだ無い
        var tempFile = Assert.Single(filesWhileWriting!);
        Assert.Matches(@"^\.photo_[0-9a-f]{32}\.tmp$", Path.GetFileName(tempFile));
        Assert.Equal(new[] { path }, Directory.GetFiles(temp.Path));
    }

    // ---------------------------------------------------------------- E14: 既にあるパス

    [Theory]
    [MemberData(nameof(Overloads))]
    public void Write_ExistingFile_ThrowsAndKeepsContents(string overload)
    {
        using var temp = new TempFolder();
        var path = temp.WriteFile("exists.jpg", new byte[] { 9, 9, 9 });

        Assert.Throws<IOException>(() => Write(overload, path, Contents));

        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Write_ExistingFileWithDifferentCase_Throws()
    {
        using var temp = new TempFolder();
        temp.WriteFile("exists.jpg", new byte[] { 9 });

        Assert.Throws<IOException>(() => SafeFileWriter.Write(temp.Combine("EXISTS.JPG"), Contents, Array.Empty<string>()));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Write_ExistingFolder_Throws()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("folder.jpg");
        Directory.CreateDirectory(path);

        Assert.Throws<IOException>(() => SafeFileWriter.Write(path, Contents, Array.Empty<string>()));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Write_FileCreatedWhileWriting_IsNotOverwritten()
    {
        // 確認した後、リネームするまでの間にほかのプロセスが同じ名前のファイルを作った場合
        using var temp = new TempFolder();
        var path = temp.Combine("race.jpg");

        Assert.Throws<IOException>(() => SafeFileWriter.Write(path, stream =>
        {
            stream.Write(Contents);
            File.WriteAllBytes(path, new byte[] { 7, 7 });
        }, Array.Empty<string>()));

        Assert.Equal(new byte[] { 7, 7 }, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(temp.Path)); // 一時ファイルは消えている
    }

    // ---------------------------------------------------------------- E14: 書き込み中の例外

    [Fact]
    public void Write_ExceptionWhileWriting_LeavesNoFiles()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("broken.jpg");

        var ex = Assert.Throws<InvalidDataException>(() => SafeFileWriter.Write(path, stream =>
        {
            stream.Write(Contents);
            throw new InvalidDataException("エンコードに失敗");
        }, Array.Empty<string>()));

        Assert.Equal("エンコードに失敗", ex.Message); // 元の例外がそのまま伝わる
        Assert.Empty(Directory.GetFiles(temp.Path)); // 書き出し先も一時ファイルも無い
    }

    [Fact]
    public void Write_OutputFolderMissing_ThrowsAndCreatesNothing()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "missing", "photo.jpg");

        Assert.Throws<DirectoryNotFoundException>(() => SafeFileWriter.Write(path, Contents, Array.Empty<string>()));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "missing")));
    }

    // ---------------------------------------------------------------- E14: 保護するパス

    [Theory]
    [MemberData(nameof(Overloads))]
    public void Write_ProtectedPath_ThrowsAndKeepsSource(string overload)
    {
        using var temp = new TempFolder();
        var source = temp.WriteFile("photo.jpg", new byte[] { 1, 2, 3 });

        Assert.Throws<InvalidOperationException>(() => Write(overload, source, Contents, source));

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Write_ProtectedPathWrittenDifferently_IsStillDetected()
    {
        // 大文字小文字・".."・"/" の違いがあっても、フルパスが同じなら書かない
        using var temp = new TempFolder();
        var sub = Directory.CreateDirectory(temp.Combine("sub")).FullName;
        var source = temp.WriteFile("Photo.JPG", new byte[] { 1 });
        var variants = new[]
        {
            temp.Combine("photo.jpg"),
            Path.Combine(sub, "..", "Photo.JPG"),
            source.Replace('\\', '/'),
        };

        foreach (var variant in variants)
        {
            Assert.Throws<InvalidOperationException>(() => SafeFileWriter.Write(variant, Contents, new[] { source }));
            Assert.Throws<InvalidOperationException>(() => SafeFileWriter.Write(source, Contents, new[] { variant }));
        }
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(source));
    }

    [Fact]
    public void Write_ProtectedPathThatDoesNotExist_IsStillProtected()
    {
        // 元画像が消えていても、そのパスには書かない（既にあるかどうかより先に確かめる）
        using var temp = new TempFolder();
        var source = temp.Combine("moved.jpg");

        Assert.Throws<InvalidOperationException>(() => SafeFileWriter.Write(source, Contents, new[] { source }));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void Write_OtherProtectedPaths_DoNotPreventWriting()
    {
        using var temp = new TempFolder();
        var source = temp.WriteFile("photo.jpg", new byte[] { 1 });
        var path = temp.Combine("photo_20261115_120000.jpg");

        SafeFileWriter.Write(path, Contents, new[] { source, "", null!, temp.Combine("other.png") });

        Assert.Equal(Contents, File.ReadAllBytes(path));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(source));
    }

    // ---------------------------------------------------------------- 引数

    [Fact]
    public void Write_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => SafeFileWriter.Write("", Contents, Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() => SafeFileWriter.Write(null!, Contents, Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() => SafeFileWriter.Write(@"C:\a.jpg", Contents, null!));
        Assert.Throws<ArgumentNullException>(() => SafeFileWriter.Write(@"C:\a.jpg", (Action<Stream>)null!, Array.Empty<string>()));
    }
}
