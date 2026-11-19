using System.Reflection;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>UI テストを実行するかどうかと、使う場所（exe・テスト画像・出力先・設定）</summary>
public static class UiTestEnvironment
{
    /// <summary>この環境変数が "1" のときだけ UI テストを実行する</summary>
    public const string VariableName = "WEBDEZA_UITEST";

    /// <summary>webDeza のアプリ名（%APPDATA% の下のフォルダ名・ウィンドウのタイトル）</summary>
    public const string WebDezaName = "webDeza";

    /// <summary>CoverCropper のアプリ名</summary>
    public const string CoverCropperName = "CoverCropper";

    /// <summary>環境変数の値から、スキップする理由を決める。実行するときは null</summary>
    public static string? SkipReason(string? value) =>
        value == "1"
            ? null
            : $"UI テストはマウスとキーボードを動かすので、環境変数 {VariableName}=1 のときだけ実行する（設計書 6.5）";

    /// <summary>今の環境変数で UI テストを実行するか</summary>
    public static bool IsEnabled => SkipReason(Environment.GetEnvironmentVariable(VariableName)) is null;

    /// <summary>リポジトリのルート（ビルドのときに csproj が書き込む）</summary>
    public static string RepositoryRoot => Metadata("RepositoryRoot");

    /// <summary>このテストをビルドした構成（Debug / Release）。同じ構成の exe を操作する</summary>
    public static string Configuration => Metadata("Configuration");

    public static string WebDezaExe => AppExe("WebDeza", "webDeza.exe");

    public static string CoverCropperExe => AppExe("CoverCropper", "CoverCropper.exe");

    /// <summary>UI テストの作業フォルダ（%TEMP%\webDeza-uitest）</summary>
    public static string WorkFolder => Path.Combine(Path.GetTempPath(), "webDeza-uitest");

    /// <summary>テスト画像の置き場所</summary>
    public static string ImagesFolder => Path.Combine(WorkFolder, "images");

    /// <summary>保存の出力先（テストごとに空にする）</summary>
    public static string OutputFolder => Path.Combine(WorkFolder, "out");

    /// <summary>CoverCropper の保存先（テストごとに空にする）</summary>
    public static string CoverCropperOutputFolder => Path.Combine(WorkFolder, "out-cc");

    /// <summary>%APPDATA%\{アプリ名}（設定とログの場所。設計書 F-12・R03）</summary>
    public static string AppDataFolder(string appName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);

    /// <summary>画面のキャプチャの置き場所（失敗したときに見るため）</summary>
    public static string ShotsFolder => Path.Combine(WorkFolder, "shots");

    /// <summary>設定ファイルの退避先</summary>
    public static string SettingsBackupFolder => Path.Combine(WorkFolder, "settings-backup");

    /// <summary>テスト画像のパス（例: Image("formats", "sample.jpg")）</summary>
    public static string Image(params string[] parts) => Path.Combine([ImagesFolder, .. parts]);

    /// <summary>%APPDATA%\{アプリ名}\settings.json（アプリと同じ決め方。設計書 F-12）</summary>
    public static string SettingsFile(string appName) => Path.Combine(AppDataFolder(appName), "settings.json");

    private static string AppExe(string projectFolder, string exeName) =>
        Path.Combine(RepositoryRoot, projectFolder, "bin", Configuration, "net10.0-windows", exeName);

    private static string Metadata(string key) =>
        typeof(UiTestEnvironment).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value
        ?? throw new InvalidOperationException($"アセンブリに {key} が書かれていない");
}
