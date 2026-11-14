namespace WebDeza.Imaging;

/// <summary>
/// 縦横比（幅:高さ）。約分して持つので、1024:768 と 4:3 は等しい。
/// default（0:0）は「比率なし」を表す。
/// </summary>
public readonly record struct AspectRatio
{
    public AspectRatio(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int divisor = GreatestCommonDivisor(width, height);
        Width = width / divisor;
        Height = height / divisor;
    }

    public int Width { get; }

    public int Height { get; }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }
}
