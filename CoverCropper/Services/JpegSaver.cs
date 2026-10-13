using System.IO;
using System.Windows.Media.Imaging;

namespace CoverCropper.Services;

public interface IJpegSaver
{
    void Save(BitmapSource image, string path, int quality);
}

/// <summary>
/// JPEG 保存。同フォルダの一時ファイルに書き込んでからリネームするため、
/// 途中で失敗しても壊れた JPEG は残らない。既存ファイルは決して上書きしない。
/// </summary>
public sealed class JpegSaver : IJpegSaver
{
    public void Save(BitmapSource image, string path, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
            throw new IOException($"ファイルが既に存在します: {fullPath}");

        var folder = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("保存先フォルダを特定できません。", nameof(path));
        var tempPath = Path.Combine(folder, $".{Path.GetFileNameWithoutExtension(fullPath)}_{Guid.NewGuid():N}.tmp");

        try
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            // メタデータ（EXIF 等）は付けない: サムネイル・メタデータ・カラーコンテキストすべて null
            encoder.Frames.Add(BitmapFrame.Create(image, null, null, null));

            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
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
