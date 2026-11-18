using System.Diagnostics;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>UI テストをまとめる集まり。テスト画像と設定の退避を 1 回で済ませる</summary>
[CollectionDefinition(Name)]
public sealed class UiCollection : ICollectionFixture<UiSession>
{
    public const string Name = "UI";
}

/// <summary>
/// UI テストの前と後の準備（設計書 6.5）。
/// 前: 2 つのアプリが動いていないことを確かめ、設定ファイルを退避し（前の実行の退避が残っていれば先に戻す）、
/// テスト画像を用意し、出力先を空にする。後: 設定ファイルを戻す。
/// UI テストを実行しないとき（環境変数が無いとき）は何もしない
/// </summary>
public sealed class UiSession : IDisposable
{
    private readonly SettingsBackup? backup;

    public UiSession()
    {
        if (!UiTestEnvironment.IsEnabled) return;
        Dpi.EnsureThread();

        // 動いているアプリがあると、終了のときに設定を書くので、退避と戻しが食い違う。利用者の編集を消さないよう、止めずに失敗にする
        var running = new[] { UiTestEnvironment.WebDezaName, UiTestEnvironment.CoverCropperName }
            .Where(name => Process.GetProcessesByName(name).Length > 0).ToList();
        if (running.Count > 0)
            throw new InvalidOperationException($"{string.Join("・", running)} が起動している。閉じてから UI テストを実行する（設定を退避して戻すため）");
        foreach (var exe in new[] { UiTestEnvironment.WebDezaExe, UiTestEnvironment.CoverCropperExe })
            if (!File.Exists(exe)) throw new FileNotFoundException($"exe が無い。先に {UiTestEnvironment.Configuration} でビルドする", exe);

        backup = new SettingsBackup(UiTestEnvironment.SettingsBackupFolder,
        [
            UiTestEnvironment.SettingsFile(UiTestEnvironment.WebDezaName),
            UiTestEnvironment.SettingsFile(UiTestEnvironment.CoverCropperName),
        ]);
        RestoredLeftover = backup.Backup();
        try
        {
            ImagesCreated = TestImageCache.Ensure(UiTestEnvironment.ImagesFolder, TestImageFactory.Version,
                root => TestImageFactory.Generate(root, ImageSet.All));
        }
        catch
        {
            backup.Restore();
            throw;
        }
    }

    /// <summary>前の実行で戻せなかった設定を、始める前に戻したか</summary>
    public bool RestoredLeftover { get; }

    /// <summary>テスト画像を作り直したか</summary>
    public bool ImagesCreated { get; }

    /// <summary>HEIC のテスト画像を作れたか（HEIF のエンコーダーと HEVC の拡張機能がある環境だけ）</summary>
    public static bool HasHeic => File.Exists(UiTestEnvironment.Image("formats", "sample.heic"));

    /// <summary>出力先を空にする（テストごとに呼ぶ）</summary>
    public static void ClearOutput()
    {
        if (Directory.Exists(UiTestEnvironment.OutputFolder)) Directory.Delete(UiTestEnvironment.OutputFolder, recursive: true);
        Directory.CreateDirectory(UiTestEnvironment.OutputFolder);
    }

    public void Dispose() => backup?.Restore();
}

/// <summary>
/// UI テストのクラスの元。起動したアプリをテストの終わりに止める。
/// 派生したクラスにも [Collection(UiCollection.Name)] を付ける（属性は継承されないため）
/// </summary>
public abstract class UiTestBase : IDisposable
{
    private readonly List<IDisposable> apps = [];

    protected UiTestBase(ITestOutputHelper output)
    {
        Check = new Checks(output);
        if (UiTestEnvironment.IsEnabled) Dpi.EnsureThread();
    }

    /// <summary>手順の 1 行ずつの確認（最後に Check.AssertAll() を呼ぶ）</summary>
    protected Checks Check { get; }

    /// <summary>設定をテストの前の状態にし、出力先を空にしてから、webDeza を起動する</summary>
    protected WebDezaApp LaunchWebDeza(params string[] args) => LaunchWebDezaFrom(null, args);

    /// <summary>LaunchWebDeza で画像を開き、すべて開き終わるまで待つ</summary>
    protected WebDezaApp OpenInWebDeza(params string[] files)
    {
        var app = LaunchWebDeza(files);
        if (!Waits.Until(() => app.DocumentTitles().Count == files.Length, TimeSpan.FromSeconds(60)))
            throw new TimeoutException($"画像が開き終わらない（{app.DocumentTitles().Count} / {files.Length} 件）");
        Thread.Sleep(800);
        return app;
    }

    /// <summary>LaunchWebDeza と同じ。「開く」のダイアログが最初に開くフォルダ（前回のフォルダ）を lastOpenFolder にする</summary>
    protected WebDezaApp LaunchWebDezaFrom(string? lastOpenFolder, params string[] args)
    {
        WebDezaSettings.Reset(lastOpenFolder);
        UiSession.ClearOutput();
        return LaunchAnotherWebDeza(args);
    }

    /// <summary>設定と出力先をそのままにして、webDeza をもう 1 つ起動する</summary>
    protected WebDezaApp LaunchAnotherWebDeza(params string[] args)
    {
        var app = WebDezaApp.Launch(args);
        apps.Add(app);
        return app;
    }

    /// <summary>テスト用の空の作業フォルダ（%TEMP%\webDeza-uitest\work\{name}）。元の画像を変えないよう、写しを置いて使う</summary>
    protected static string FreshFolder(string name)
    {
        string folder = Path.Combine(UiTestEnvironment.WorkFolder, "work", name);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>ファイルの操作を試し、失敗したら理由を返す（成功したら null）</summary>
    protected static string? TryFileOperation(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>失敗したときに見るため、取り込んだ絵を shots フォルダに書く</summary>
    protected static void SaveShot(Pixels pixels, string name) =>
        pixels.SavePng(Path.Combine(UiTestEnvironment.ShotsFolder, name + ".png"));

    public void Dispose()
    {
        foreach (var app in apps) app.Dispose();
        GC.SuppressFinalize(this);
    }
}
