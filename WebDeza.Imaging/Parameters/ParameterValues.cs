using System.Globalization;

namespace WebDeza.Imaging.Parameters;

/// <summary>
/// パラメーターの値の集まり（キー → 値）。フィルターの値と、出力形式のオプションの値に使う。
/// 不変で、中身で比較できる。値は long・double・bool・string のどれかなので、JSON にそのまま書ける。
/// </summary>
/// <remarks>
/// 数値は値の大きさで比べる（整数の 85 と実数の 85.0 は等しい）。
/// JSON に書いて読み直すと 1.0 が 1 になることがあっても、同じ値として扱える。
/// </remarks>
public sealed class ParameterValues : IEquatable<ParameterValues>
{
    private const double MaxExactInteger = 9007199254740992; // 2^53

    private readonly Dictionary<string, object> _values;

    private ParameterValues(Dictionary<string, object> values) => _values = values;

    /// <summary>値を 1 つも持たないもの</summary>
    public static ParameterValues Empty { get; } = new(new Dictionary<string, object>(StringComparer.Ordinal));

    /// <summary>記述の既定値をすべて入れたもの</summary>
    public static ParameterValues Defaults(IEnumerable<ParameterDescriptor> descriptors)
        => Empty.Normalize(descriptors);

    public int Count => _values.Count;

    /// <summary>キーの一覧（文字コード順。JSON に書くときに順番がぶれないようにする）</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            var keys = _values.Keys.ToArray();
            Array.Sort(keys, StringComparer.Ordinal);
            return keys;
        }
    }

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <summary>値をそのまま返す（long・double・bool・string のどれか）。JSON に書くときに使う</summary>
    /// <exception cref="KeyNotFoundException">キーが無い</exception>
    public object this[string key]
        => _values.TryGetValue(key, out object? value)
            ? value
            : throw new KeyNotFoundException($"パラメーター「{key}」がありません。");

    /// <summary>
    /// 値を型を指定して取り出す。数値は int・long・float・double のどれでも取り出せる
    /// （実数を整数として取り出せるのは、小数部が無いときだけ）。
    /// </summary>
    /// <exception cref="KeyNotFoundException">キーが無い</exception>
    /// <exception cref="InvalidCastException">値を T として取り出せない</exception>
    public T Get<T>(string key)
    {
        object value = this[key];
        if (value is T typed) return typed;

        object? converted = null;
        if (typeof(T) == typeof(double))
        {
            if (value is long l) converted = (double)l;
        }
        else if (typeof(T) == typeof(float))
        {
            if (value is long l) converted = (float)l;
            else if (value is double d) converted = (float)d;
        }
        else if (typeof(T) == typeof(long))
        {
            // double が整数を正確に表せるのは ±2^53 までなので、その範囲に限る
            if (value is double d && IsWholeNumberIn(d, -MaxExactInteger, MaxExactInteger)) converted = (long)d;
        }
        else if (typeof(T) == typeof(int))
        {
            if (value is long l && l is >= int.MinValue and <= int.MaxValue) converted = (int)l;
            else if (value is double d && IsWholeNumberIn(d, int.MinValue, int.MaxValue)) converted = (int)d;
        }

        if (converted is null)
            throw new InvalidCastException(
                $"パラメーター「{key}」の値（{Format(value)}）は {typeof(T).Name} として取り出せません。");
        return (T)converted;
    }

    /// <summary>
    /// 値を 1 つ変えた（または足した）ものを返す。元のものは変わらない。
    /// 値は整数（long にする）・実数（double にする）・bool・string のどれか。
    /// </summary>
    public ParameterValues With(string key, object value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!TryCanonicalize(value, out object canonical))
            throw new ArgumentException(
                $"パラメーターの値にできるのは、整数・有限の実数・bool・string だけです（{value.GetType().Name}）。", nameof(value));

        var values = new Dictionary<string, object>(_values, StringComparer.Ordinal) { [key] = canonical };
        return new ParameterValues(values);
    }

    /// <summary>
    /// 記述に合わせたものを返す。無いキーは既定値で補い、範囲外の値は範囲に丸め、
    /// 種類に合わない値は既定値にし、記述に無いキーは捨てる。元のものは変わらない。
    /// </summary>
    public ParameterValues Normalize(IEnumerable<ParameterDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            _values.TryGetValue(descriptor.Key, out object? current);
            if (!values.TryAdd(descriptor.Key, descriptor.Coerce(current)))
                throw new ArgumentException($"パラメーターのキー「{descriptor.Key}」が重複しています。", nameof(descriptors));
        }
        return new ParameterValues(values);
    }

    // ---------------------------------------------------------------- 比較

    public bool Equals(ParameterValues? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_values.Count != other._values.Count) return false;

        foreach (var (key, value) in _values)
        {
            if (!other._values.TryGetValue(key, out object? otherValue) || !ValueEquals(value, otherValue))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ParameterValues);

    public override int GetHashCode()
    {
        // キーの順番によらない値にする
        int hash = _values.Count;
        foreach (var (key, value) in _values)
            hash ^= HashCode.Combine(key, ValueHashCode(value));
        return hash;
    }

    public static bool operator ==(ParameterValues? left, ParameterValues? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ParameterValues? left, ParameterValues? right) => !(left == right);

    /// <summary>ログやテストの失敗メッセージ用（例: {interlace=false, quality=85}）</summary>
    public override string ToString()
        => "{" + string.Join(", ", Keys.Select(key => $"{key}={Format(_values[key])}")) + "}";

    // ---------------------------------------------------------------- 値の扱い

    /// <summary>
    /// 値を、持ち方をそろえた形（整数は long、実数は double、bool、string）にする。
    /// 値にできない型（null・NaN・無限大を含む）のときは false。
    /// </summary>
    internal static bool TryCanonicalize(object? value, out object canonical)
    {
        switch (value)
        {
            case long or bool or string:
                canonical = value;
                return true;
            case int or short or sbyte or byte or ushort or uint:
                canonical = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            case double d when double.IsFinite(d):
                canonical = d;
                return true;
            case float f when float.IsFinite(f):
                canonical = (double)f;
                return true;
            case decimal m:
                canonical = (double)m;
                return true;
            default:
                canonical = value!;
                return false;
        }
    }

    private static bool ValueEquals(object a, object b) => (a, b) switch
    {
        (long x, long y) => x == y,
        (long x, double y) => x == y,
        (double x, long y) => x == y,
        (double x, double y) => x.Equals(y),
        _ => a.Equals(b),
    };

    /// <summary>等しい数値（85 と 85.0）が同じ値になるよう、数値は double にしてから求める</summary>
    private static int ValueHashCode(object value) => value switch
    {
        long l => ((double)l).GetHashCode(),
        _ => value.GetHashCode(),
    };

    private static bool IsWholeNumberIn(double value, double min, double max)
        => value >= min && value <= max && Math.Floor(value) == value;

    private static string Format(object value) => value switch
    {
        bool b => b ? "true" : "false",
        string s => $"\"{s}\"",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
