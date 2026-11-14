using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WebDeza.Imaging.Tests;

/// <summary>
/// 画像ファイルを WPF（WIC）で読んで ImageBuffer にする、テスト用の補助。
/// エンジンはファイル形式を扱わないので、基準ファイル（PNG）を読むのにだけ使う。
/// </summary>
internal static class WpfImageFiles
{
    /// <summary>CoverCropper の基準ファイル（CoverCropper.Tests/Baseline/expected をリンクで参照したもの）のパス</summary>
    public static string BaselineExpectedPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Baseline", "expected", fileName);

    /// <summary>1 枚目を Bgra32 にして読む（向きの情報は見ない）</summary>
    public static ImageBuffer Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapSource frame = BitmapDecoder.Create(
            stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if (frame.Format != PixelFormats.Bgra32)
            frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        int stride = frame.PixelWidth * 4;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);
        return ImageBuffer.Create(frame.PixelWidth, frame.PixelHeight, pixels);
    }
}
