namespace WebDeza.UiTests.Infrastructure;

/// <summary>
/// テスト画像の置き場所を用意する。画像は大きい（合わせて約 100 MB）のでリポジトリに入れず、テストの中で作る（設計書 6.5）。
/// 作り直すのは、無いときと、画像を作るコードの版が変わったときだけ
/// </summary>
public static class TestImageCache
{
    /// <summary>版を書いておくファイル。作り終えてから書く（これがあれば、画像がそろっている）</summary>
    public const string StampFileName = "version.txt";

    /// <summary>
    /// root に version の画像がそろっていなければ、generate で作り直す（作ったら true）。
    /// 一時フォルダに作ってから置き換えるので、途中で失敗しても、中途半端な画像が残らない
    /// </summary>
    public static bool Ensure(string root, string version, Action<string> generate)
    {
        string stamp = Path.Combine(root, StampFileName);
        if (File.Exists(stamp) && File.ReadAllText(stamp) == version) return false;

        string temp = root + ".tmp";
        if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        Directory.CreateDirectory(temp);
        try
        {
            generate(temp);
            File.WriteAllText(Path.Combine(temp, StampFileName), version);
        }
        catch
        {
            Directory.Delete(temp, recursive: true);
            throw;
        }

        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.Move(temp, root);
        return true;
    }
}
