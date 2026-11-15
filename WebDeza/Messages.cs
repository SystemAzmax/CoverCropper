namespace WebDeza;

/// <summary>設計書 4.7 のメッセージ一覧（M03・M08・M10・M13・M18・M19・M22・M23 は欠番）</summary>
public static class Messages
{
    public const string M01_UnsupportedFormat = "対応していないファイル形式です。";
    public static string M02_LoadFailed(string detail) => $"画像を読み込めませんでした。（{detail}）";
    public static string M04_Exported(string fileName, string size) => $"書き出しました: {fileName}（{size}）";
    public const string M05_OutputFolderNotSet = "出力先フォルダが未設定です。フォルダを選択してください。";
    public static string M06_OutputFolderNotFound(string path) => $"出力先フォルダが見つかりません: {path}";
    public static string M07_ExportFailed(string detail) => $"書き出しに失敗しました。（{detail}）";

    public static string M09_Opened(int count, int unsupported)
        => unsupported > 0
            ? $"{count} 件を開きました。非対応 {unsupported} 件を除きました。"
            : $"{count} 件を開きました。";

    public static string M11_BatchFinished(int succeeded, int failed, string totalSize)
        => $"一括書き出しが終わりました。成功 {succeeded} 件 ／ 失敗 {failed} 件 ／ 合計 {totalSize}";

    public static string M12_BatchCancelled(int succeeded) => $"一括書き出しを中止しました（{succeeded} 件書き出し済み）。";
    public const string M14_FolderHasNoImages = "フォルダに対応する画像がありませんでした。";
    public static string M15_OverLimit(int max, int notOpened) => $"同時に開けるのは {max} 枚までです。{notOpened} 件は開きませんでした。";
    public static string M16_ConfirmClose(string fileName) => $"「{fileName}」には書き出していない編集があります。閉じますか？";
    public static string M16_ConfirmCloseAll(int count) => $"書き出していない編集がある画像が {count} 枚あります。すべて閉じますか？";
    public const string M17_ConfirmExit = "書き出していない編集があります。終了しますか？";
    public const string M20_SettingsLoadFailed = "設定ファイルを読み込めなかったため、既定値で起動しました。";
    public static string M21_Requirement(string requirement) => $"この形式を読むには {requirement} が必要です。";
    public static string M24_AlphaFlattened(string formatName) => $"{formatName} は透過を保てないため、透過部分を背景色で塗ります。";
    public static string M25_FormatNotFound(string formatId) => $"形式「{formatId}」が見つからないため、JPEG にしました。";
    public static string M26_OverSizeLimit(string limit, string size) => $"品質をいちばん低くしても上限（{limit}）を超えます（{size}）。";
    public const string M27_CannotDecodeResult = "この形式は読み直せないため、圧縮後の画像を表示できません（サイズは表示します）。";
    public static string M28_EditFailed(string detail) => $"編集できませんでした。（{detail}）";

    /// <summary>M29: 開けなかった画像（1 行目に件数、続けてファイル名と理由を 1 行ずつ）</summary>
    public static string M29_OpenFailed(IReadOnlyList<(string FileName, string Reason)> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var lines = new List<string> { $"{failures.Count} 件を開けませんでした。" };
        lines.AddRange(failures.Select(f => $"{f.FileName}: {f.Reason}"));
        return string.Join("\n", lines);
    }

    /// <summary>M02・M07・M28 の詳細に入れる、メモリが足りないときの文（CoverCropper と同じ）</summary>
    public const string OutOfMemory = "メモリが不足しています";

    /// <summary>画像が無いときに、ワークスペースの中央に出す案内文（設計書 F-02）</summary>
    public const string Placeholder = "ここに画像やフォルダをドラッグ＆ドロップ、または［開く］";

    /// <summary>未処理の例外のときのダイアログ（CoverCropper と同じ文面）</summary>
    public static string UnexpectedError(string detail, string logFile)
        => $"予期しないエラーが発生しました。\n{detail}\n\n詳細はログを確認してください:\n{logFile}";
}
