using System.IO;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;

namespace WebDeza.Services;

/// <summary>一括保存の 1 件（設計書 5.6）</summary>
/// <param name="SourcePath">元画像のパス（ファイル名に使い、書き込みから守る）</param>
/// <param name="Image">保存する今の画像（編集を確定した結果の全体）</param>
public sealed record BatchJob(string SourcePath, ImageBuffer Image);

/// <summary>一括保存の 1 件の結果</summary>
/// <param name="SourcePath">元画像のパス</param>
/// <param name="SavedPath">保存したファイルのパス。失敗したら null</param>
/// <param name="Bytes">保存したファイルのバイト数（失敗したら 0）</param>
/// <param name="Error">失敗した理由（成功なら null）</param>
public sealed record BatchItemResult(string SourcePath, string? SavedPath, long Bytes, string? Error)
{
    /// <summary>保存できたか</summary>
    public bool Succeeded => SavedPath is not null;

    /// <summary>元画像のファイル名（M11 の失敗の一覧・進捗の文に出す）</summary>
    public string FileName => Path.GetFileName(SourcePath);
}

/// <summary>一括保存の進み具合（1 件終わるたびに知らせる）</summary>
/// <param name="Done">終わった件数</param>
/// <param name="Total">全体の件数</param>
/// <param name="Last">今終わった 1 件の結果</param>
public sealed record BatchProgress(int Done, int Total, BatchItemResult? Last);

/// <summary>一括保存の結果</summary>
/// <param name="Items">処理した分の結果（順に。中止・出力先が無くなって止めた後の分は入らない）</param>
/// <param name="Cancelled">中止したか（M12）</param>
/// <param name="OutputFolderMissing">失敗したときに出力先フォルダが無くなっていて、止めたか（M06）</param>
/// <param name="TotalBytes">保存できたファイルのバイト数の合計（M11）</param>
public sealed record BatchResult(IReadOnlyList<BatchItemResult> Items, bool Cancelled, bool OutputFolderMissing, long TotalBytes)
{
    /// <summary>保存できた件数</summary>
    public int SucceededCount => Items.Count(i => i.Succeeded);

    /// <summary>失敗した件数</summary>
    public int FailedCount => Items.Count(i => !i.Succeeded);
}

/// <summary>
/// 一括保存（設計書 F-11・5.6）: 開いている画像の今の中身を、同じ保存の設定で、1 件ずつ順に保存する
/// （メモリを抑えるため並列にしない）。1 件の保存は ExportService.Export（保存する画像を作る → エンコード → 元画像を守って書く。
/// 上限があれば画像ごとに品質を探す）。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>中止は 1 件ごとの区切りで確かめる。保存中の 1 件は最後まで終えてから止める（その 1 件には取り消しを渡さない）</item>
/// <item>失敗したら、ログに書いて次へ進む。ただし、そのとき出力先フォルダが無くなっていたら、残りも失敗するので止める</item>
/// <item>進み具合は、1 件終わるたびに IProgress で知らせる</item>
/// </list>
/// 出力先が未設定・無いことは、呼ぶ側（保存のダイアログ）が始める前に確かめる（F-10 と同じ。M05 / M06）。
/// </remarks>
public sealed class BatchExporter
{
    private readonly ExportService _exporter;
    private readonly AppLog _log;

    public BatchExporter(ExportService exporter, AppLog log)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(log);
        _exporter = exporter;
        _log = log;
    }

    /// <summary>jobs を順に保存する（バックグラウンド）。中止しても例外は投げず、Cancelled の結果を返す</summary>
    /// <param name="jobs">保存する画像（この順に保存する）</param>
    /// <param name="outputFolder">出力先のフォルダ</param>
    /// <param name="settings">保存の設定（すべての画像で同じ）</param>
    /// <param name="progress">進み具合の知らせ先（null なら知らせない）</param>
    /// <param name="cancellationToken">中止（1 件ごとの区切りで確かめる）</param>
    public Task<BatchResult> RunAsync(IReadOnlyList<BatchJob> jobs, string outputFolder, ExportSettings settings,
        IProgress<BatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentException.ThrowIfNullOrEmpty(outputFolder);
        ArgumentNullException.ThrowIfNull(settings);
        return Task.Run(() => Run(jobs, outputFolder, settings, progress, cancellationToken), CancellationToken.None);
    }

    private BatchResult Run(IReadOnlyList<BatchJob> jobs, string outputFolder, ExportSettings settings,
        IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        var items = new List<BatchItemResult>(jobs.Count);
        bool cancelled = false, folderMissing = false;

        foreach (var job in jobs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            BatchItemResult item;
            try
            {
                // 保存中の 1 件は最後まで終える（取り消しを渡さない）
                var result = _exporter.Export(job.Image, job.SourcePath, outputFolder, settings, CancellationToken.None);
                item = new BatchItemResult(job.SourcePath, result.Path, result.FileSize, null);
            }
            catch (OutOfMemoryException ex)
            {
                _log.Error($"Batch export OOM: {job.SourcePath}", ex);
                item = new BatchItemResult(job.SourcePath, null, 0, Messages.OutOfMemory);
            }
            catch (Exception ex)
            {
                // 書けない・形式プラグインの例外など。ログに書いて次へ進む（設計書 F-11・5.10-6）
                _log.Error($"Batch export failed: {job.SourcePath} -> {outputFolder}", ex);
                item = new BatchItemResult(job.SourcePath, null, 0, ex.Message);
            }

            items.Add(item);
            progress?.Report(new BatchProgress(items.Count, jobs.Count, item));

            // 失敗したときに出力先フォルダが無くなっていたら、残りも失敗するので止める（M06）
            if (!item.Succeeded && !Directory.Exists(outputFolder))
            {
                folderMissing = true;
                break;
            }
        }

        return new BatchResult(items, cancelled, folderMissing, items.Where(i => i.Succeeded).Sum(i => i.Bytes));
    }
}
