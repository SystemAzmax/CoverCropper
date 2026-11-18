using System.Text.Json;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>
/// アプリの設定ファイルを退避して、後で戻す（設計書 6.5）。製品のコードは変えず、本物の %APPDATA% のまま確かめるため。
/// 退避の一覧（manifest.json）は、退避の後・戻し終わるまで残る。テストが途中で落ちて戻せなかったときは、
/// 次の <see cref="Backup"/> で、先にそれを戻してから退避し直す（テストで書き換えた設定を、本物として退避しないため）
/// </summary>
public sealed class SettingsBackup(string folder, IReadOnlyList<string> files)
{
    private const string ManifestName = "manifest.json";

    private string ManifestPath => Path.Combine(folder, ManifestName);

    /// <summary>戻していない退避が残っているか</summary>
    public bool HasPending => File.Exists(ManifestPath);

    /// <summary>設定ファイルを退避する。前の退避が残っていたら、先にそれを戻す（戻したら true）</summary>
    public bool Backup()
    {
        bool restoredLeftover = HasPending;
        if (restoredLeftover) Restore();

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        var entries = new List<Entry>();
        for (int i = 0; i < files.Count; i++)
        {
            string copy = $"{i}.bak";
            bool existed = File.Exists(files[i]);
            if (existed) File.Copy(files[i], Path.Combine(folder, copy));
            entries.Add(new Entry(files[i], existed, copy));
        }

        // 一覧は最後に書く（一覧があれば、退避がそろっている）
        string temp = ManifestPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries));
        File.Move(temp, ManifestPath);
        return restoredLeftover;
    }

    /// <summary>退避した設定ファイルを戻す。もとは無かったファイルは消す。退避が無ければ何もしない</summary>
    public void Restore()
    {
        if (!HasPending) return;
        var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(ManifestPath))
                      ?? throw new InvalidDataException("退避の一覧が読めない: " + ManifestPath);
        foreach (var entry in entries)
        {
            if (entry.Existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(entry.Path)!);
                File.Copy(Path.Combine(folder, entry.BackupName), entry.Path, overwrite: true);
            }
            else if (File.Exists(entry.Path))
            {
                File.Delete(entry.Path);
            }
        }

        // 一覧は最後に消す（途中で落ちても、次にもう一度戻せる）
        File.Delete(ManifestPath);
        foreach (var entry in entries)
        {
            string copy = Path.Combine(folder, entry.BackupName);
            if (File.Exists(copy)) File.Delete(copy);
        }
    }

    private sealed record Entry(string Path, bool Existed, string BackupName);
}
