namespace WebDeza.Imaging.Filters;

/// <summary>
/// フィルターの登録先。登録順が、編集パネルに並ぶ順番と、フィルターをかける順番になる。
/// </summary>
public sealed class FilterRegistry
{
    private readonly List<IImageFilter> _filters = new();

    /// <summary>登録順の一覧</summary>
    public IReadOnlyList<IImageFilter> All => _filters;

    /// <summary>フィルターを登録する</summary>
    /// <exception cref="ArgumentException">Id が空、または同じ Id のフィルターが登録済み</exception>
    public void Register(IImageFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (string.IsNullOrEmpty(filter.Id))
            throw new ArgumentException("フィルターの Id が空です。", nameof(filter));
        if (Find(filter.Id) is not null)
            throw new ArgumentException($"Id「{filter.Id}」のフィルターは登録済みです。", nameof(filter));

        _filters.Add(filter);
    }

    /// <summary>Id でフィルターを探す（大文字小文字を区別する）。無ければ null</summary>
    public IImageFilter? Find(string id)
        => _filters.Find(f => string.Equals(f.Id, id, StringComparison.Ordinal));
}
