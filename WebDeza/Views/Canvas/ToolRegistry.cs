namespace WebDeza.Views.Canvas;

/// <summary>
/// 道具の登録先（設計書 1.3・5.6）。登録順に、道具のボタンが並ぶ。最初に登録した道具が既定の道具になる。
/// </summary>
public sealed class ToolRegistry
{
    private readonly List<ICanvasToolFactory> _tools = new();

    /// <summary>登録順の一覧</summary>
    public IReadOnlyList<ICanvasToolFactory> All => _tools;

    /// <summary>道具を登録する</summary>
    /// <exception cref="ArgumentException">Id が空、または同じ Id の道具が登録済み</exception>
    public void Register(ICanvasToolFactory tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (string.IsNullOrEmpty(tool.Id))
            throw new ArgumentException("道具の Id が空です。", nameof(tool));
        if (Find(tool.Id) is not null)
            throw new ArgumentException($"Id「{tool.Id}」の道具は登録済みです。", nameof(tool));

        _tools.Add(tool);
    }

    /// <summary>Id で道具を探す（大文字小文字を区別する）。無ければ null</summary>
    public ICanvasToolFactory? Find(string id)
        => _tools.Find(t => string.Equals(t.Id, id, StringComparison.Ordinal));
}
