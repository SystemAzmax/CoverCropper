using System.Globalization;

namespace WebDeza.Core;

/// <summary>
/// 書き出すファイルのパスを作る。名前は「元名_yyyyMMdd_HHmmss.{拡張子}」で、
/// 既に使われているときは _2, _3 … を付ける（既にあるファイルを上書きしないため）。
/// </summary>
public sealed class OutputFileNamer
{
    private readonly Func<string, bool> _exists;

    public OutputFileNamer() : this(p => File.Exists(p) || Directory.Exists(p)) { }

    /// <param name="exists">パスが既に使われているかの判定（テスト用に差し替え可能）</param>
    public OutputFileNamer(Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        _exists = exists;
    }

    /// <param name="outputFolder">書き出し先のフォルダ</param>
    /// <param name="sourcePath">元画像のパス（拡張子を除いた名前を使う）</param>
    /// <param name="now">名前に入れる日時</param>
    /// <param name="extension">拡張子（".jpg" でも "jpg" でもよい）</param>
    public string CreatePath(string outputFolder, string sourcePath, DateTime now, string extension)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputFolder);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(extension);
        if (extension[0] != '.') extension = "." + extension;

        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "image";

        var stem = $"{baseName}_{now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}";
        var candidate = Path.Combine(outputFolder, stem + extension);

        for (int i = 2; _exists(candidate); i++)
            candidate = Path.Combine(outputFolder, $"{stem}_{i}{extension}");

        return candidate;
    }
}
