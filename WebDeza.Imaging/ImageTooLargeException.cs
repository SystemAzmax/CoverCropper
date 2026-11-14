namespace WebDeza.Imaging;

/// <summary>
/// 幅 × 高さ × 4 バイトが .NET の配列の上限（約 2 GB）を超えるため、ImageBuffer にできない画像。
/// </summary>
public sealed class ImageTooLargeException : Exception
{
    public ImageTooLargeException(int width, int height)
        : base($"画像が大きすぎます（{width}×{height}）。")
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }
}
