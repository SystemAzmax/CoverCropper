using System.Collections.Frozen;
using System.Text;
using WebDeza.Imaging.Geometry;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Editing;

/// <summary>
/// 画像ごとの編集内容（レシピ）。不変。表示と書き出しのたびに、元画像とこれから作り直す（非破壊編集）。
/// </summary>
/// <param name="Orientation">回転・反転</param>
/// <param name="Crop">切り抜きの枠（編集画像のピクセル座標）。null のときは初期状態の枠</param>
/// <param name="Filters">フィルターの Id → 値。無いフィルターは既定値として扱う</param>
/// <remarks>
/// 比較は Filters の中身で行う（record の既定は辞書を参照で比べてしまうため）。
/// 「無いフィルター」と「既定値を入れたフィルター」は、中身が違うので等しくならない。
/// </remarks>
public sealed record EditRecipe(
    ImageOrientation Orientation,
    PixelRect? Crop,
    IReadOnlyDictionary<string, ParameterValues> Filters)
{
    private readonly IReadOnlyDictionary<string, ParameterValues> _filters = ParameterValuesMap.Freeze(Filters, nameof(Filters));

    /// <summary>初期状態（そのままの向き・初期状態の枠・フィルターなし）</summary>
    public static EditRecipe Initial { get; } = new(
        ImageOrientation.Identity, null, FrozenDictionary<string, ParameterValues>.Empty);

    /// <summary>フィルターの Id → 値（複製して持つので、渡した辞書を後で変えても影響しない）</summary>
    public IReadOnlyDictionary<string, ParameterValues> Filters
    {
        get => _filters;
        init => _filters = ParameterValuesMap.Freeze(value, nameof(Filters));
    }

    /// <summary>フィルターの値。無ければ空（フィルターの既定値として扱われる）</summary>
    public ParameterValues GetFilter(string filterId)
        => _filters.GetValueOrDefault(filterId) ?? ParameterValues.Empty;

    /// <summary>フィルターの値を 1 つ変えたレシピ</summary>
    public EditRecipe WithFilter(string filterId, ParameterValues values)
        => this with { Filters = ParameterValuesMap.With(_filters, filterId, values) };

    public bool Equals(EditRecipe? other)
        => other is not null
           && Orientation == other.Orientation
           && Crop == other.Crop
           && ParameterValuesMap.ContentEquals(_filters, other._filters);

    public override int GetHashCode()
        => HashCode.Combine(Orientation, Crop, ParameterValuesMap.ContentHashCode(_filters));

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Orientation = {Orientation}, Crop = {Crop?.ToString() ?? "null"}, ");
        builder.Append($"Filters = {ParameterValuesMap.Format(_filters)}");
        return true;
    }
}
