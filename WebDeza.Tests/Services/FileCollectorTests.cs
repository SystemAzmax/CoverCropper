using System.IO;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Services;

namespace WebDeza.Tests.Services;

/// <summary>開くファイルの決定（設計書 F-01・5.6。V12〜V15。実装計画書 7-5）。ファイルの中身は読まないので、空のファイルで確かめる</summary>
public sealed class FileCollectorTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FileCollector _collector;

    public FileCollectorTests()
    {
        var formats = new FormatRegistry();
        formats.AddWicFormats();
        _collector = new FileCollector(formats);
    }

    public void Dispose() => _temp.Dispose();

    private string CreateFile(params string[] names)
    {
        var path = _temp.Combine(names);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    private string CreateFolder(params string[] names) => Directory.CreateDirectory(_temp.Combine(names)).FullName;

    private CollectResult Collect(IEnumerable<string> paths, IEnumerable<string>? open = null, int capacity = 50)
        => _collector.Collect(paths, open ?? [], capacity);

    private static string[] Names(IEnumerable<string> paths) => paths.Select(Path.GetFileName).ToArray()!;

    // ---------------------------------------------------------------- V12: フォルダ

    [Fact]
    public void Folder_OnlySupportedFilesDirectlyInside()
    {
        var folder = CreateFolder("photos");
        CreateFile("photos", "a.jpg");
        CreateFile("photos", "b.PNG");
        CreateFile("photos", "memo.txt");
        CreateFile("photos", "sub", "c.jpg");

        var result = Collect([folder]);

        Assert.Equal(new[] { "a.jpg", "b.PNG" }, Names(result.Files));
        Assert.Equal(0, result.Unsupported); // フォルダの中の非対応のファイルは数えない
        Assert.False(result.FolderHadNoImages);
    }

    [Fact]
    public void Folder_WithoutImages_FolderHadNoImages()
    {
        var folder = CreateFolder("empty");
        CreateFile("empty", "memo.txt");
        CreateFile("empty", "sub", "c.jpg");

        var result = Collect([folder]);

        Assert.Empty(result.Files);
        Assert.True(result.FolderHadNoImages);
        Assert.Equal(0, result.Unsupported);
    }

    [Fact]
    public void FolderAndFiles_AreMixed()
    {
        var folder = CreateFolder("photos");
        CreateFile("photos", "b.jpg");
        var file = CreateFile("a.jpg");

        var result = Collect([folder, file]);

        Assert.Equal(new[] { file, Path.Combine(folder, "b.jpg") }, result.Files);
    }

    [Fact]
    public void EmptyFolderWithImageFolder_IsNotFolderHadNoImages()
    {
        var empty = CreateFolder("empty");
        var photos = CreateFolder("photos");
        CreateFile("photos", "a.jpg");

        var result = Collect([empty, photos]);

        Assert.Single(result.Files);
        Assert.False(result.FolderHadNoImages);
    }

    // ---------------------------------------------------------------- V13: 既に開いている・重複・非対応

    [Fact]
    public void AlreadyOpen_IsNotReopened()
    {
        var a = CreateFile("a.jpg");
        var b = CreateFile("b.jpg");

        var result = Collect([a, b], open: [a.ToUpperInvariant()]);

        Assert.Equal(new[] { b }, result.Files);
        Assert.Equal(new[] { a }, result.AlreadyOpen);
    }

    [Fact]
    public void Duplicates_AreMergedIgnoringCase()
    {
        var a = CreateFile("a.jpg");
        var folder = _temp.Path;

        // 同じファイルを、大文字小文字違い・相対の書き方・フォルダ経由で渡す
        var result = Collect([a, a.ToUpperInvariant(), Path.Combine(folder, ".", "a.jpg"), folder]);

        Assert.Equal(new[] { a }, result.Files);
    }

    [Fact]
    public void Unsupported_AreExcludedAndCounted()
    {
        var a = CreateFile("a.jpg");

        var result = Collect([a, CreateFile("memo.txt"), CreateFile("data.bin")]);

        Assert.Equal(new[] { a }, result.Files);
        Assert.Equal(2, result.Unsupported);
        Assert.False(result.FolderHadNoImages);
    }

    [Fact]
    public void MissingFileWithSupportedExtension_IsKept()
    {
        // 開くときに読めず M02 になる（ここではファイルの有無を問わない）
        var missing = _temp.Combine("missing.jpg");

        var result = Collect([missing]);

        Assert.Equal(new[] { missing }, result.Files);
    }

    [Fact]
    public void RelativePath_BecomesFullPath()
    {
        var a = CreateFile("a.jpg");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, a);

        var result = Collect([relative]);

        // 今のフォルダのドライブ名の大文字小文字に従うので、大文字小文字を区別せずに比べる
        Assert.Equal(a, Assert.Single(result.Files), ignoreCase: true);
        Assert.True(Path.IsPathFullyQualified(result.Files[0]));
    }

    // ---------------------------------------------------------------- V14: 自然順

    [Fact]
    public void Files_AreInNaturalOrder()
    {
        var paths = new[] { CreateFile("a10.jpg"), CreateFile("a2.jpg"), CreateFile("a1.jpg") };

        var result = Collect(paths);

        Assert.Equal(new[] { "a1.jpg", "a2.jpg", "a10.jpg" }, Names(result.Files));
    }

    [Fact]
    public void Files_AreSortedByFullPath()
    {
        // フルパスで並べるので、フォルダ名で先に分かれる
        var paths = new[] { CreateFile("f10", "a.jpg"), CreateFile("f2", "z.jpg"), CreateFile("f2", "b.jpg") };

        var result = Collect(paths);

        Assert.Equal(new[] { paths[2], paths[1], paths[0] }, result.Files);
    }

    [Theory]
    [InlineData("a1", "a2", -1)]
    [InlineData("a2", "a10", -1)]
    [InlineData("A10", "a2", 1)]
    [InlineData("abc", "ABC", 0)]
    public void NaturalStringComparer_ComparesNumbersAsNumbers(string x, string y, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(NaturalStringComparer.Instance.Compare(x, y)));
    }

    // ---------------------------------------------------------------- D&D で受け入れるか（7-6）

    [Fact]
    public void ContainsOpenable_SupportedFileOrFolder()
    {
        var image = CreateFile("a.jpg");
        var text = CreateFile("memo.txt");
        var emptyFolder = CreateFolder("empty");

        Assert.True(_collector.ContainsOpenable([image]));
        Assert.True(_collector.ContainsOpenable([text, image])); // 1 つでも含めばよい
        Assert.True(_collector.ContainsOpenable([emptyFolder])); // フォルダの中は見ない（画像が無ければ M14）
        Assert.True(_collector.ContainsOpenable([text, emptyFolder]));
    }

    [Fact]
    public void ContainsOpenable_NothingToOpen()
    {
        Assert.False(_collector.ContainsOpenable([CreateFile("memo.txt"), CreateFile("data.bin")]));
        Assert.False(_collector.ContainsOpenable([_temp.Combine("missing.jpg")])); // 無いファイル
        Assert.False(_collector.ContainsOpenable([]));
        Assert.False(_collector.ContainsOpenable(["", "  "]));
        Assert.False(_collector.ContainsOpenable(null));
    }

    // ---------------------------------------------------------------- V15: 上限

    [Fact]
    public void OverCapacity_ReturnsOnlyRoomAndCountsRest()
    {
        var paths = Enumerable.Range(1, 5).Select(i => CreateFile($"p{i}.jpg")).Reverse().ToArray();

        var result = Collect(paths, capacity: 2);

        Assert.Equal(new[] { "p1.jpg", "p2.jpg" }, Names(result.Files)); // 自然順の前から
        Assert.Equal(3, result.OverLimit);
    }

    [Fact]
    public void AlreadyOpen_DoesNotUseCapacity()
    {
        var a = CreateFile("a.jpg");
        var b = CreateFile("b.jpg");

        var result = Collect([a, b], open: [a], capacity: 1);

        Assert.Equal(new[] { b }, result.Files);
        Assert.Equal(new[] { a }, result.AlreadyOpen);
        Assert.Equal(0, result.OverLimit);
    }

    [Fact]
    public void NoCapacity_NothingToOpen()
    {
        var result = Collect([CreateFile("a.jpg")], capacity: 0);

        Assert.Empty(result.Files);
        Assert.Equal(1, result.OverLimit);
    }
}
