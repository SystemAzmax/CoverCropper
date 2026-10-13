using System.Globalization;
using System.IO;

namespace CoverCropper.Services;

public interface IOutputFileNamer
{
    /// <summary>元名_yyyyMMdd_HHmmss.jpg、重複時は _2, _3...</summary>
    string CreatePath(string outputFolder, string sourcePath, DateTime now);
}

public sealed class OutputFileNamer : IOutputFileNamer
{
    private readonly Func<string, bool> _exists;

    public OutputFileNamer() : this(p => File.Exists(p) || Directory.Exists(p)) { }

    /// <param name="exists">パスが既に使われているかの判定（テスト用に差し替え可能）</param>
    public OutputFileNamer(Func<string, bool> exists) => _exists = exists;

    public string CreatePath(string outputFolder, string sourcePath, DateTime now)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputFolder);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);

        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "image";

        var stem = $"{baseName}_{now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}";
        var candidate = Path.Combine(outputFolder, stem + ".jpg");

        for (int i = 2; _exists(candidate); i++)
            candidate = Path.Combine(outputFolder, $"{stem}_{i}.jpg");

        return candidate;
    }
}
