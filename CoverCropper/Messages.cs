namespace CoverCropper;

/// <summary>設計書 3.3 のメッセージ一覧</summary>
public static class Messages
{
    public const string M01_UnsupportedFormat = "対応していないファイル形式です。";
    public static string M02_LoadFailed(string detail) => $"画像を読み込めませんでした。（{detail}）";
    public const string M03_Upscaled = "元画像より拡大しています（画質が低下する可能性があります）。";
    public static string M04_Saved(string fileName) => $"保存しました: {fileName}";
    public const string M05_OutputFolderNotSet = "保存先フォルダが未設定です。フォルダを選択してください。";
    public static string M06_OutputFolderNotFound(string path) => $"保存先フォルダが見つかりません: {path}";
    public static string M07_SaveFailed(string detail) => $"保存に失敗しました。（{detail}）";
    public const string M08_FirstFileOnly = "最初の1件のみ読み込みました。";

    public const string SettingsLoadFailed = "設定ファイルを読み込めなかったため、既定値で起動しました。";
    public const string AskChangeOutputFolder = "保存先フォルダを選択し直しますか？";
    public const string SameAsSourcePath = "保存先が元画像と同じパスのため保存を中止しました。";
    public const string OutOfMemory = "メモリが不足しています";
    public const string Placeholder = "ここに画像をドラッグ＆ドロップ、または［開く］";
    public const string NotSet = "（未設定）";
}
