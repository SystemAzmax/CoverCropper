using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.InfrastructureTests;

/// <summary>設定ファイルの退避と戻し（一時フォルダで確かめる。本物の %APPDATA% には触れない）</summary>
public class SettingsBackupTests
{
    [Fact]
    public void BackupThenRestore_RestoresChangedFiles()
    {
        using var temp = new TempFolder();
        string a = temp.Combine("appA", "settings.json"), b = temp.Combine("appB", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllText(a, "A の本物");
        File.WriteAllText(b, "B の本物");
        var backup = new SettingsBackup(temp.Combine("backup"), [a, b]);

        Assert.False(backup.Backup());
        Assert.True(backup.HasPending);
        File.WriteAllText(a, "テストで書き換えた");
        File.Delete(b);
        backup.Restore();

        Assert.Equal("A の本物", File.ReadAllText(a));
        Assert.Equal("B の本物", File.ReadAllText(b));
        Assert.False(backup.HasPending);
    }

    [Fact]
    public void Restore_FileThatDidNotExist_IsDeleted()
    {
        using var temp = new TempFolder();
        string a = temp.Combine("appA", "settings.json"); // フォルダも無い
        var backup = new SettingsBackup(temp.Combine("backup"), [a]);

        backup.Backup();
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        File.WriteAllText(a, "テストで作った");
        backup.Restore();

        Assert.False(File.Exists(a));
    }

    [Fact]
    public void Backup_WithLeftoverFromCrashedRun_RestoresItFirst()
    {
        using var temp = new TempFolder();
        string a = temp.Combine("settings.json");
        File.WriteAllText(a, "本物");
        string folder = temp.Combine("backup");

        // 前の実行: 退避してから書き換え、戻す前に落ちた
        new SettingsBackup(folder, [a]).Backup();
        File.WriteAllText(a, "テストで書き換えた");

        // 次の実行: 先に戻してから退避するので、テストで書き換えた設定を本物として退避しない
        var next = new SettingsBackup(folder, [a]);
        Assert.True(next.Backup());
        Assert.Equal("本物", File.ReadAllText(a));
        File.WriteAllText(a, "次のテストで書き換えた");
        next.Restore();

        Assert.Equal("本物", File.ReadAllText(a));
        Assert.False(next.HasPending);
    }

    [Fact]
    public void Restore_WithoutBackup_DoesNothing()
    {
        using var temp = new TempFolder();
        string a = temp.Combine("settings.json");
        File.WriteAllText(a, "そのまま");

        new SettingsBackup(temp.Combine("backup"), [a]).Restore();

        Assert.Equal("そのまま", File.ReadAllText(a));
    }
}
