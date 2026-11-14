namespace WebDeza.Imaging;

/// <summary>
/// 8 bit Bgra32（ストレートアルファ）の画像。1 画素は B・G・R・A の 4 バイトで、stride は Width * 4。
/// 作った後は中身を変えない約束とする（表示や履歴と共有されうるため。処理は入力を変えずに新しい ImageBuffer を返す）。
/// </summary>
public sealed class ImageBuffer
{
    private const int BytesPerPixel = 4;

    private ImageBuffer(int width, int height, byte[] pixels, bool isOpaque)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        IsOpaque = isOpaque;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>画素の並び（長さは Width * Height * 4）。Create に渡した配列そのもの</summary>
    public byte[] Pixels { get; }

    /// <summary>すべてのアルファが 255 なら true（作るときに決める）</summary>
    public bool IsOpaque { get; }

    /// <summary>
    /// 画素の配列から画像を作る。配列は複製せずにそのまま持つので、渡した後は書き換えないこと。
    /// </summary>
    /// <param name="isOpaque">不透明かどうかが分かっているときに渡す。null なら全画素のアルファを調べて決める</param>
    /// <exception cref="ImageTooLargeException">幅 × 高さ × 4 が配列の上限を超える</exception>
    /// <exception cref="ArgumentException">配列の長さが 幅 × 高さ × 4 と合わない</exception>
    public static ImageBuffer Create(int width, int height, byte[] pixels, bool? isOpaque = null)
    {
        int length = GetByteLength(width, height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != length)
            throw new ArgumentException(
                $"画素の配列の長さ（{pixels.Length}）が {width}×{height} の画像の長さ（{length}）と合いません。", nameof(pixels));

        return new ImageBuffer(width, height, pixels, isOpaque ?? HasNoTransparency(pixels));
    }

    /// <summary>
    /// 幅 × 高さの画像に要る配列の長さ（幅 × 高さ × 4）を返す。配列を作る前の検証に使う。
    /// </summary>
    /// <exception cref="ImageTooLargeException">長さが配列の上限（Array.MaxLength）を超える</exception>
    public static int GetByteLength(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        // 幅 × 高さ × 4 は long でもあふれうるので、画素数の段階で上限と比べる
        if ((long)width * height > Array.MaxLength / BytesPerPixel)
            throw new ImageTooLargeException(width, height);

        return width * height * BytesPerPixel;
    }

    private static bool HasNoTransparency(byte[] pixels)
    {
        for (int i = BytesPerPixel - 1; i < pixels.Length; i += BytesPerPixel)
        {
            if (pixels[i] != 255) return false;
        }
        return true;
    }
}
