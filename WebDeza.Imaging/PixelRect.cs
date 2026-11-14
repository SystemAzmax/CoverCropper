namespace WebDeza.Imaging;

/// <summary>画像のピクセル座標（整数）での矩形</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
