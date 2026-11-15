namespace WebDeza.Imaging.IO;

/// <summary>
/// 書き出したファイルを安全に書く。画像を書くのはこのクラスだけとする（元画像を変えないため。設計書 1.4）。
/// </summary>
/// <remarks>
/// 同じフォルダの一時ファイル（.{名前}_{GUID}.tmp）に書いてからリネームするので、途中で失敗しても壊れたファイルは残らない。
/// 既にあるファイルは決して上書きしない。CoverCropper の JpegSaver の動作を一般化したもの。
/// </remarks>
public static class SafeFileWriter
{
    /// <summary>contents をファイルに書く</summary>
    /// <param name="path">書き出すパス</param>
    /// <param name="contents">ファイルの中身</param>
    /// <param name="protectedPaths">書いてはいけないパス（元画像）。フルパスで比べ、大文字小文字は区別しない</param>
    /// <exception cref="InvalidOperationException">path が protectedPaths のどれかと同じ</exception>
    /// <exception cref="IOException">path に既にファイルかフォルダがある（中身は変えない）</exception>
    /// <exception cref="DirectoryNotFoundException">書き出し先のフォルダが無い</exception>
    public static void Write(string path, ReadOnlySpan<byte> contents, IEnumerable<string> protectedPaths)
    {
        var (fullPath, tempPath) = Prepare(path, protectedPaths);
        try
        {
            using (var stream = CreateTempFile(tempPath))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, fullPath, overwrite: false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>write が stream に書いた中身をファイルに書く</summary>
    /// <param name="path">書き出すパス</param>
    /// <param name="write">中身を書く処理。渡された stream は閉じないこと</param>
    /// <param name="protectedPaths">書いてはいけないパス（元画像）。フルパスで比べ、大文字小文字は区別しない</param>
    /// <exception cref="InvalidOperationException">path が protectedPaths のどれかと同じ</exception>
    /// <exception cref="IOException">path に既にファイルかフォルダがある（中身は変えない）</exception>
    /// <exception cref="DirectoryNotFoundException">書き出し先のフォルダが無い</exception>
    public static void Write(string path, Action<Stream> write, IEnumerable<string> protectedPaths)
    {
        ArgumentNullException.ThrowIfNull(write);
        var (fullPath, tempPath) = Prepare(path, protectedPaths);
        try
        {
            using (var stream = CreateTempFile(tempPath))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, fullPath, overwrite: false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>書く前の確認をして、書き出すフルパスと一時ファイルのパスを返す</summary>
    private static (string FullPath, string TempPath) Prepare(string path, IEnumerable<string> protectedPaths)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(protectedPaths);

        var fullPath = Path.GetFullPath(path);

        // 安全装置: 元画像のパスには絶対に書き込まない（既にあるかどうかより先に確かめる）
        foreach (var protectedPath in protectedPaths)
        {
            if (string.IsNullOrEmpty(protectedPath)) continue;
            if (string.Equals(Path.GetFullPath(protectedPath), fullPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"書き出し先が元画像と同じパスのため、書き出しを中止しました: {fullPath}");
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw new IOException($"ファイルが既に存在します: {fullPath}");

        var folder = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("書き出し先のフォルダを特定できません。", nameof(path));
        var tempPath = Path.Combine(folder, $".{Path.GetFileNameWithoutExtension(fullPath)}_{Guid.NewGuid():N}.tmp");
        return (fullPath, tempPath);
    }

    private static FileStream CreateTempFile(string tempPath)
        => new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 削除できなくても元の例外を優先する
        }
    }
}
