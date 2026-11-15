using System.IO;
using WebDeza.Imaging.Formats;

namespace WebDeza.Services;

/// <summary>
/// FileCollector の結果（設計書 5.6）
/// </summary>
/// <param name="Files">新しく開くファイル（フルパス。自然順）</param>
/// <param name="AlreadyOpen">既に開いている画像のファイル（フルパス。自然順。開き直さず、その画像を選ぶ）</param>
/// <param name="Unsupported">指定されたファイルのうち、対応していない形式の件数（フォルダの中の非対応のファイルは数えない）</param>
/// <param name="OverLimit">同時に開ける数を超えたため、開かない件数</param>
/// <param name="FolderHadNoImages">フォルダが指定され、そのどれにも対応する画像が無かった</param>
public sealed record CollectResult(IReadOnlyList<string> Files, IReadOnlyList<string> AlreadyOpen,
                                   int Unsupported, int OverLimit, bool FolderHadNoImages);

/// <summary>
/// 「開く」・D&amp;D・コマンドライン引数で渡されたパスを、開く画像ファイルの並びにする（設計書 F-01・5.6）。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>フォルダは、直下の対応ファイルを開く（サブフォルダは見ない）</item>
/// <item>対応しているかどうかは、形式レジストリの登録内容（拡張子）で決める</item>
/// <item>フルパスにしてから、大文字小文字を区別せずに重複を 1 つにする。既に開いている画像は AlreadyOpen に分ける</item>
/// <item>フルパスの自然順（エクスプローラーと同じ）に並べ、同時に開ける数を超えた分は後ろから外す</item>
/// </list>
/// ファイルの中身は読まない（読めるかどうかは、開くときに分かる）。
/// </remarks>
public sealed class FileCollector
{
    private readonly FormatRegistry _formats;

    public FileCollector(FormatRegistry formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = formats;
    }

    /// <param name="paths">渡されたパス（ファイルかフォルダ）</param>
    /// <param name="openPaths">既に開いている画像のフルパス</param>
    /// <param name="capacity">あと何枚開けるか</param>
    public CollectResult Collect(IEnumerable<string> paths, IEnumerable<string> openPaths, int capacity)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(openPaths);

        var open = new HashSet<string>(openPaths, StringComparer.OrdinalIgnoreCase);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int unsupported = 0;
        bool hadFolder = false;
        bool folderHadImages = false;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                unsupported++; // パスとして使えないものは、開けないファイルとして数える
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                hadFolder = true;
                foreach (var file in EnumerateSupportedFiles(fullPath))
                {
                    folderHadImages = true;
                    candidates.Add(file);
                }
            }
            else if (_formats.CanDecode(fullPath))
            {
                // 無いファイルもここに入る（開くときに M02 で知らせる）
                candidates.Add(fullPath);
            }
            else
            {
                unsupported++;
            }
        }

        var sorted = candidates.Order(NaturalStringComparer.Instance).ToList();
        var alreadyOpen = sorted.Where(open.Contains).ToList();
        var files = sorted.Where(p => !open.Contains(p)).ToList();
        int room = Math.Max(0, capacity);
        int overLimit = Math.Max(0, files.Count - room);
        if (overLimit > 0) files = files.Take(room).ToList();

        return new CollectResult(files, alreadyOpen, unsupported, overLimit, hadFolder && !folderHadImages);
    }

    /// <summary>フォルダ直下の対応ファイル（読めないフォルダは、画像が無いものとする）</summary>
    private IEnumerable<string> EnumerateSupportedFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder).Where(_formats.CanDecode).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
