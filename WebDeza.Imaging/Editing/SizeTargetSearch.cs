namespace WebDeza.Imaging.Editing;

/// <summary>
/// ファイルサイズの上限に収まる、いちばん大きい値（JPEG の品質など）を探す。
/// </summary>
public static class SizeTargetSearch
{
    /// <summary>
    /// encode(v) の長さが limit 以下になる最大の v を、[min, max] の範囲から二分探索で探す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 長さが limit 以下だったものだけを候補にするので、Fits が true の結果は必ず limit 以下になる
    /// （値とサイズが単調でない場合も、上限は守る。そのときは、いちばん大きい値でないことがある）。
    /// </para>
    /// <para>
    /// まず max を試し、収まればそれで終わる（上限に余裕があるときは 1 回で済む）。
    /// エンコードの回数は ceil(log2(max − min + 1)) + 1 回以内（1〜100 なら 8 回以内）。同じ値を 2 回エンコードしない。
    /// </para>
    /// <para>1 つも収まらなければ min の結果を返し、Fits = false。</para>
    /// </remarks>
    /// <param name="min">値の最小</param>
    /// <param name="max">値の最大</param>
    /// <param name="limit">長さの上限（バイト）</param>
    /// <param name="encode">値でエンコードしたバイト列を返す処理</param>
    /// <param name="cancellationToken">取り消し（エンコードの前ごとに確かめる）</param>
    public static (int Value, byte[] Bytes, bool Fits) FindLargest(
        int min, int max, long limit, Func<int, byte[]> encode, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min, max);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        ArgumentNullException.ThrowIfNull(encode);

        byte[]? minBytes = null;
        byte[] Encode(int value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = encode(value) ?? throw new InvalidOperationException($"値 {value} のエンコード結果が null です。");
            if (value == min) minBytes = bytes;
            return bytes;
        }

        // いちばん大きい値が収まれば、それで終わる
        var maxBytes = Encode(max);
        if (maxBytes.LongLength <= limit) return (max, maxBytes, true);

        // [min, max − 1] で、収まるいちばん大きい値を探す。
        // 収まったら上へ、収まらなかったら下へ。1 つも収まらなければ、最後に min を試したところで終わる
        int? bestValue = null;
        byte[]? bestBytes = null;
        int low = min, high = max - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            var bytes = Encode(middle);
            if (bytes.LongLength <= limit)
            {
                bestValue = middle;
                bestBytes = bytes;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (bestValue is { } value) return (value, bestBytes!, true);

        // 1 つも収まらない: min の結果（探索の最後に試している。min == max のときは最初に試している）
        return (min, minBytes ?? Encode(min), false);
    }
}
