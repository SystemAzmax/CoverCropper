using System.Windows.Media.Imaging;
using WebDeza.Imaging;
using WebDeza.Imaging.Wic;

namespace CoverCropper.Services;

public interface IImageCropper
{
    /// <summary>枠位置を画像内にクランプ（純粋関数）</summary>
    (int X, int Y) Clamp(int x, int y, int imageWidth, int imageHeight);

    /// <summary>枠を画像中央に置いたときの位置（純粋関数）</summary>
    (int X, int Y) Center(int imageWidth, int imageHeight);

    /// <summary>指定位置から 1024×768 を切り出す（Freeze 済み・96DPI・Bgr24）</summary>
    BitmapSource Crop(BitmapSource source, int x, int y);
}

public sealed class ImageCropper : IImageCropper
{
    public (int X, int Y) Clamp(int x, int y, int imageWidth, int imageHeight)
    {
        int maxX = Math.Max(0, imageWidth - AppConstants.TargetWidth);
        int maxY = Math.Max(0, imageHeight - AppConstants.TargetHeight);
        return (Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
    }

    public (int X, int Y) Center(int imageWidth, int imageHeight)
        => Clamp((imageWidth - AppConstants.TargetWidth) / 2,
                 (imageHeight - AppConstants.TargetHeight) / 2,
                 imageWidth, imageHeight);

    public BitmapSource Crop(BitmapSource source, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.PixelWidth < AppConstants.TargetWidth || source.PixelHeight < AppConstants.TargetHeight)
            throw new ArgumentException(
                $"画像サイズ {source.PixelWidth}×{source.PixelHeight} が切り抜きサイズより小さいです。", nameof(source));

        (x, y) = Clamp(x, y, source.PixelWidth, source.PixelHeight);

        // 切り出しは共通エンジンの Crop。96DPI・Bgr24 にして返す
        var rect = new PixelRect(x, y, AppConstants.TargetWidth, AppConstants.TargetHeight);
        var cropped = WebDeza.Imaging.Operations.Crop.Apply(BitmapSourceConverter.FromBitmapSource(source), rect);
        return BitmapSourceConverter.ToBgr24BitmapSource(cropped);
    }
}
