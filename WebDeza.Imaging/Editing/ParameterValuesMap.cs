using System.Collections.Frozen;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Editing;

/// <summary>
/// 「Id → ParameterValues」の辞書（レシピのフィルターの値、書き出し設定の形式ごとのオプション）を扱う補助。
/// 渡された辞書は複製して変えられない形にし、比較は中身で行う。
/// </summary>
internal static class ParameterValuesMap
{
    public static IReadOnlyDictionary<string, ParameterValues> Freeze(
        IReadOnlyDictionary<string, ParameterValues> source, string paramName)
    {
        ArgumentNullException.ThrowIfNull(source, paramName);
        foreach (var (key, value) in source)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("キーが空です。", paramName);
            if (value is null)
                throw new ArgumentException($"「{key}」の値が null です。", paramName);
        }
        return source.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static bool ContentEquals(
        IReadOnlyDictionary<string, ParameterValues> a, IReadOnlyDictionary<string, ParameterValues> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || !value.Equals(other)) return false;
        }
        return true;
    }

    /// <summary>キーの順番によらないハッシュ</summary>
    public static int ContentHashCode(IReadOnlyDictionary<string, ParameterValues> map)
    {
        int hash = map.Count;
        foreach (var (key, value) in map)
            hash ^= HashCode.Combine(key, value);
        return hash;
    }

    public static IReadOnlyDictionary<string, ParameterValues> With(
        IReadOnlyDictionary<string, ParameterValues> map, string key, ParameterValues values)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(values);
        var copy = new Dictionary<string, ParameterValues>(map, StringComparer.Ordinal) { [key] = values };
        return copy;
    }

    public static string Format(IReadOnlyDictionary<string, ParameterValues> map)
        => "{" + string.Join(", ", map.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}")) + "}";
}
