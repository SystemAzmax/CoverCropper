namespace WebDeza.UiTests.Infrastructure;

/// <summary>
/// 手動テストの手順の 1 行ずつの確認。失敗しても止めずに最後まで確かめ、終わりにまとめて失敗にする
/// （UI テストは 1 回が長いので、1 回の実行で NG をすべて見られるようにする）。結果はテストの出力に [OK] / [NG] で出す
/// </summary>
public sealed class Checks(ITestOutputHelper output)
{
    private readonly List<string> failures = [];

    public bool That(string label, bool condition, string detail = "")
    {
        string line = $"[{(condition ? "OK" : "NG")}] {label}{(detail.Length > 0 ? "  — " + detail : "")}";
        output.WriteLine(line);
        if (!condition) failures.Add(line);
        return condition;
    }

    /// <summary>判定しない情報（かかった時間など）を出力に書く</summary>
    public void Note(string message) => output.WriteLine("  " + message);

    /// <summary>NG があれば失敗にする</summary>
    public void AssertAll()
    {
        if (failures.Count > 0) Assert.Fail($"NG {failures.Count} 件:\n" + string.Join("\n", failures));
    }
}
