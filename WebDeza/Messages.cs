namespace WebDeza;

/// <summary>設計書 4.5 のメッセージ一覧（M08 は欠番）</summary>
public static class Messages
{
    public const string M01_UnsupportedFormat = "対応していないファイル形式です。";
    public static string M02_LoadFailed(string detail) => $"画像を読み込めませんでした。（{detail}）";
    public const string M03_Upscaled = "切り抜いた範囲より大きく書き出すため拡大します（画質が低下する可能性があります）。";
    public static string M04_Exported(string fileName, string size) => $"書き出しました: {fileName}（{size}）";
    public const string M05_OutputFolderNotSet = "出力先フォルダが未設定です。フォルダを選択してください。";
    public static string M06_OutputFolderNotFound(string path) => $"出力先フォルダが見つかりません: {path}";
    public static string M07_ExportFailed(string detail) => $"書き出しに失敗しました。（{detail}）";

    public static string M09_Added(int count, int duplicates, int unsupported)
        => duplicates > 0 || unsupported > 0
            ? $"{count} 件を一覧に追加しました。重複 {duplicates} 件・非対応 {unsupported} 件を除きました。"
            : $"{count} 件を一覧に追加しました。";

    public static string M10_ConfirmExportAll(int count, int alreadyExported)
        => alreadyExported > 0
            ? $"一覧の {count} 件を書き出します。よろしいですか？\n書き出し済みの {alreadyExported} 件も新しいファイルとして書き出します。"
            : $"一覧の {count} 件を書き出します。よろしいですか？";

    public static string M11_BatchFinished(int succeeded, int failed, string totalSize)
        => $"一括書き出しが終わりました。成功 {succeeded} 件 ／ 失敗 {failed} 件 ／ 合計 {totalSize}";

    public static string M12_BatchCancelled(int succeeded) => $"一括書き出しを中止しました（{succeeded} 件書き出し済み）。";
    public const string M13_ConfirmApplyFiltersToAll = "表示中の画像のフィルターの値を、一覧のすべての画像に適用しますか？";
    public const string M14_FolderHasNoImages = "フォルダに対応する画像がありませんでした。";
    public static string M15_OverLimit(int max, int notAdded) => $"一覧に入れられるのは {max} 件までです。{notAdded} 件は追加しませんでした。";
    public const string M16_ConfirmClearList = "書き出していない編集があります。一覧をクリアしますか？";
    public const string M17_ConfirmExit = "書き出していない編集があります。終了しますか？";
    public const string M18_ConfirmExitDuringBatch = "一括書き出し中です。中止して終了しますか？";
    public const string M19_InheritedFromCoverCropper = "CoverCropper の保存先フォルダを引き継ぎました。";
    public const string M20_SettingsLoadFailed = "設定ファイルを読み込めなかったため、既定値で起動しました。";
    public static string M21_Requirement(string requirement) => $"この形式を読むには {requirement} が必要です。";
    public static string M22_PresetSaved(string name) => $"プリセット「{name}」を保存しました。";
    public static string M23_ConfirmDeletePreset(string name) => $"プリセット「{name}」を削除しますか？";
    public static string M24_AlphaFlattened(string formatName) => $"{formatName} は透過を保てないため、透過部分を背景色で塗ります。";
    public static string M25_FormatNotFound(string formatId) => $"形式「{formatId}」が見つからないため、JPEG にしました。";
    public static string M26_OverSizeLimit(string limit, string size) => $"品質をいちばん低くしても上限（{limit}）を超えます（{size}）。";
    public const string M27_CannotDecodeResult = "この形式は読み直せないため、圧縮後の画像を表示できません（サイズは表示します）。";

    /// <summary>画像が無いときに、キャンバスの中央に出す案内文（設計書 4.1）</summary>
    public const string Placeholder = "ここに画像やフォルダをドラッグ＆ドロップ、または［開く］";

    /// <summary>未処理の例外のときのダイアログ（CoverCropper と同じ文面）</summary>
    public static string UnexpectedError(string detail, string logFile)
        => $"予期しないエラーが発生しました。\n{detail}\n\n詳細はログを確認してください:\n{logFile}";
}
