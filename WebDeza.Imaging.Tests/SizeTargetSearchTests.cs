using WebDeza.Imaging.Editing;

namespace WebDeza.Imaging.Tests;

public class SizeTargetSearchTests
{
    /// <summary>エンコードした値を記録する偽のエンコーダー</summary>
    private sealed class CountingEncoder(Func<int, int> sizeOf)
    {
        public List<int> Calls { get; } = new();

        public byte[] Encode(int value)
        {
            Calls.Add(value);
            var bytes = new byte[sizeOf(value)];
            if (bytes.Length > 0) bytes[0] = (byte)value; // どの値の結果か見分けられるようにする
            return bytes;
        }
    }

    private static int MaxEncodes(int min, int max) => (int)Math.Ceiling(Math.Log2(max - min + 1)) + 1;

    // ---------------------------------------------------------------- E28

    [Fact]
    public void FindLargest_LinearSize_FindsLargestFittingValue()
    {
        // サイズ = 値 × 10。上限 555 なら 55（550 バイト）
        var encoder = new CountingEncoder(v => v * 10);

        var (value, bytes, fits) = SizeTargetSearch.FindLargest(1, 100, 555, encoder.Encode);

        Assert.True(fits);
        Assert.Equal(55, value);
        Assert.Equal(550, bytes.Length);
        Assert.Equal(55, bytes[0]); // 55 でエンコードした結果そのもの
        Assert.InRange(encoder.Calls.Count, 1, 8);
    }

    [Fact]
    public void FindLargest_NothingFits_ReturnsMinWithFitsFalse()
    {
        // 上限 5: 品質 1（10 バイト）でも収まらない
        var encoder = new CountingEncoder(v => v * 10);

        var (value, bytes, fits) = SizeTargetSearch.FindLargest(1, 100, 5, encoder.Encode);

        Assert.False(fits);
        Assert.Equal(1, value);
        Assert.Equal(10, bytes.Length);
        Assert.Equal(1, bytes[0]);
        Assert.InRange(encoder.Calls.Count, 1, 8);
        Assert.Equal(encoder.Calls.Count, encoder.Calls.Distinct().Count()); // 同じ値を 2 回エンコードしない
    }

    [Theory]
    [InlineData(1000)]   // ちょうど品質 100 の大きさ
    [InlineData(5000)]
    public void FindLargest_MaxFits_EncodesOnlyOnce(long limit)
    {
        var encoder = new CountingEncoder(v => v * 10);

        var (value, _, fits) = SizeTargetSearch.FindLargest(1, 100, limit, encoder.Encode);

        Assert.True(fits);
        Assert.Equal(100, value);
        Assert.Equal(new[] { 100 }, encoder.Calls);
    }

    [Theory]
    [InlineData(10, 1)]     // 上限ちょうど（以下なので収まる）
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    [InlineData(999, 99)]
    public void FindLargest_LimitIsInclusive(long limit, int expected)
    {
        var encoder = new CountingEncoder(v => v * 10);

        var (value, bytes, fits) = SizeTargetSearch.FindLargest(1, 100, limit, encoder.Encode);

        Assert.True(fits);
        Assert.Equal(expected, value);
        Assert.True(bytes.Length <= limit);
    }

    [Fact]
    public void FindLargest_MonotonicSizes_FindsTrueLargestWithinEncodeBudget()
    {
        // 単調なら、いちばん大きい値を正しく見つけ、回数も上限以内
        var random = new Random(28);
        for (int i = 0; i < 1000; i++)
        {
            int min = random.Next(1, 50), max = min + random.Next(0, 200);
            var sizes = new int[max + 1];
            for (int v = min, size = random.Next(0, 100); v <= max; v++, size += random.Next(0, 50)) sizes[v] = size;
            long limit = random.Next(0, sizes[max] + 100);
            var encoder = new CountingEncoder(v => sizes[v]);

            var (value, bytes, fits) = SizeTargetSearch.FindLargest(min, max, limit, encoder.Encode);

            int expected = Enumerable.Range(min, max - min + 1).Where(v => sizes[v] <= limit).DefaultIfEmpty(-1).Max();
            string context = $"#{i}: [{min}, {max}]・上限 {limit}";
            if (expected < 0)
            {
                Assert.False(fits, context);
                Assert.Equal(min, value);
            }
            else
            {
                Assert.True(fits, context);
                Assert.Equal(expected, value);
            }
            Assert.Equal(sizes[value], bytes.Length);
            Assert.True(encoder.Calls.Count <= MaxEncodes(min, max), $"{context}: {encoder.Calls.Count} 回エンコードしました");
        }
    }

    // ---------------------------------------------------------------- E29

    [Fact]
    public void FindLargest_NonMonotonicSizes_NeverExceedsLimitWhenFits()
    {
        // サイズが値に対して単調でない偽のエンコーダー（実際の JPEG でも、品質を 1 上げてサイズが減ることがある）
        var random = new Random(29);
        var sizes = Enumerable.Range(0, 101).Select(_ => random.Next(100, 10_000)).ToArray();

        for (int i = 0; i < 1000; i++)
        {
            long limit = random.Next(0, 11_000);
            var encoder = new CountingEncoder(v => sizes[v]);

            var (value, bytes, fits) = SizeTargetSearch.FindLargest(1, 100, limit, encoder.Encode);

            string context = $"#{i}: 上限 {limit} → 値 {value}・{bytes.Length} バイト・Fits = {fits}";
            Assert.Equal(sizes[value], bytes.Length);
            if (fits)
                Assert.True(bytes.Length <= limit, context);
            else
                Assert.Equal(1, value);
            Assert.True(encoder.Calls.Count <= 8, $"{context}: {encoder.Calls.Count} 回エンコードしました");
            Assert.Equal(encoder.Calls.Count, encoder.Calls.Distinct().Count());
        }
    }

    // ---------------------------------------------------------------- そのほか

    [Theory]
    [InlineData(50, 50, 500, 50, true)]   // 範囲が 1 つ: 収まる
    [InlineData(50, 50, 499, 50, false)]  // 範囲が 1 つ: 収まらない
    [InlineData(1, 2, 15, 1, true)]
    [InlineData(1, 2, 5, 1, false)]
    public void FindLargest_SmallRanges(int min, int max, long limit, int expectedValue, bool expectedFits)
    {
        var encoder = new CountingEncoder(v => v * 10);

        var (value, _, fits) = SizeTargetSearch.FindLargest(min, max, limit, encoder.Encode);

        Assert.Equal((expectedValue, expectedFits), (value, fits));
        Assert.True(encoder.Calls.Count <= MaxEncodes(min, max));
    }

    [Fact]
    public void FindLargest_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SizeTargetSearch.FindLargest(10, 9, 100, v => new byte[v]));
        Assert.Throws<ArgumentOutOfRangeException>(() => SizeTargetSearch.FindLargest(1, 100, -1, v => new byte[v]));
        Assert.Throws<ArgumentNullException>(() => SizeTargetSearch.FindLargest(1, 100, 100, null!));
        Assert.Throws<InvalidOperationException>(() => SizeTargetSearch.FindLargest(1, 100, 100, _ => null!));
    }

    [Fact]
    public void FindLargest_Cancelled_StopsBeforeNextEncode()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;

        Assert.ThrowsAny<OperationCanceledException>(() => SizeTargetSearch.FindLargest(1, 100, 5, v =>
        {
            if (++calls == 3) cts.Cancel();
            return new byte[v * 10];
        }, cts.Token));

        Assert.Equal(3, calls);
    }
}
