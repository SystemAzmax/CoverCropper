using System.Windows.Media.Imaging;
using WebDeza.Imaging.Wic;

namespace CoverCropper.Services;

public interface IImageResizer
{
    /// <summary>F-02 の計算式に従ったリサイズ後サイズを返す（純粋関数）</summary>
    (int Width, int Height) CalculateSize(int srcWidth, int srcHeight);

    /// <summary>高品質補間でリサイズし、96DPI・Bgr24 の Freeze 済み画像を返す</summary>
    BitmapSource Resize(BitmapSource source, int width, int height);
}

/// <summary>
/// 比率維持リサイズ。大きさの計算（CalculateSize）は CoverCropper だけのもの。
/// 補間は共通エンジン（WebDeza.Imaging）の Lanczos3 で、縮小時はカーネル幅を縮小率に応じて広げるため
/// 大きな縮小率でもジャギーやモアレが出にくい（WPF の HighQuality(Fant) 以上の品質）。
/// スレッドに依存しない純粋なピクセル処理なので Task.Run 上で安全に実行できる。
/// </summary>
public sealed class ImageResizer : IImageResizer
{
    public (int Width, int Height) CalculateSize(int srcWidth, int srcHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(srcWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(srcHeight);

        int newW = AppConstants.TargetWidth;
        int newH = RoundToInt((double)srcHeight * AppConstants.TargetWidth / srcWidth);

        if (newH < AppConstants.TargetHeight)
        {
            newH = AppConstants.TargetHeight;
            newW = Math.Max(AppConstants.TargetWidth,
                RoundToInt((double)srcWidth * AppConstants.TargetHeight / srcHeight));
        }
        return (newW, newH);
    }

    public BitmapSource Resize(BitmapSource source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        // 補間は共通エンジンの Resize（Lanczos3）。DPI 情報は捨て、96DPI・Bgr24 にして返す
        var resized = WebDeza.Imaging.Operations.Resize.Apply(
            BitmapSourceConverter.FromBitmapSource(source), width, height);
        return BitmapSourceConverter.ToBgr24BitmapSource(resized);
    }

    private static int RoundToInt(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
