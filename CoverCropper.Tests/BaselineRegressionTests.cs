using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoverCropper.Services;
using Xunit.Abstractions;

namespace CoverCropper.Tests;

/// <summary>
/// 画像処理を共通エンジンへ置き換える前に作った基準ファイル（Baseline/）と、今の処理結果を比べる（webDeza 設計書 R05）。
/// 基準ファイルは tools/BaselineGenerator で置き換え前に作ったもので、作り直さない。
/// </summary>
public class BaselineRegressionTests
{
    private static readonly string BaselineFolder = Path.Combine(AppContext.BaseDirectory, "Baseline");

    private readonly ITestOutputHelper _output;

    public BaselineRegressionTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("landscape", "landscape_3000x1000.png", 2304, 768, 640, 0)]
    [InlineData("portrait", "portrait_1500x2000.png", 1024, 1365, 0, 298)]
    [InlineData("ratio4x3", "ratio4x3_2048x1536.png", 1024, 768, 0, 0)]
    [InlineData("small", "small_800x600.png", 1024, 768, 0, 0)]
    [InlineData("wide_small", "wide_small_500x100.png", 3840, 768, 1408, 0)]
    [InlineData("transparent", "transparent_1600x1200.png", 1024, 768, 0, 0)]
    [InlineData("orientation6", "orientation6_1600x1200.tif", 1024, 1365, 0, 298)]
    [InlineData("palette", "palette_1600x900.gif", 1365, 768, 170, 0)]
    [InlineData("gray16", "gray16_1000x1500.png", 1024, 1536, 0, 384)]
    public void Pipeline_MatchesBaseline(string name, string inputFile, int resizedWidth, int resizedHeight, int cropX, int cropY)
    {
        var loaded = new ImageLoader().Load(Path.Combine(BaselineFolder, "inputs", inputFile));
        AssertSamePixels(Decode(ExpectedPath($"{name}.loaded.png")), loaded, "読み込み");

        var resizer = new ImageResizer();
        Assert.Equal((resizedWidth, resizedHeight), resizer.CalculateSize(loaded.PixelWidth, loaded.PixelHeight));
        var resized = resizer.Resize(loaded, resizedWidth, resizedHeight);
        var expectedResized = Decode(ExpectedPath($"{name}.resized.png"));
        AssertSamePixels(expectedResized, resized, "リサイズ");

        var cropper = new ImageCropper();
        Assert.Equal((cropX, cropY), cropper.Center(resizedWidth, resizedHeight));
        var cropped = cropper.Crop(resized, cropX, cropY);
        var expectedCropped = CopyRegion(expectedResized, cropX, cropY, AppConstants.TargetWidth, AppConstants.TargetHeight);
        AssertSamePixels(expectedCropped, cropped, "切り抜き");

        using var temp = new TempFolder();
        var outputPath = temp.Combine("output.jpg");
        new JpegSaver().Save(cropped, outputPath, AppConstants.JpegQuality);
        var actualBytes = File.ReadAllBytes(outputPath);

        // JPEG のエンコード結果は OS のコーデックで変わりうるため、まずこの環境で作った参照（品質 85・メタデータなし）と比べる
        var referenceBytes = EncodeJpeg(expectedCropped, AppConstants.JpegQuality);
        Assert.True(referenceBytes.AsSpan().SequenceEqual(actualBytes), "JPEG 出力: 品質 85・メタデータなしで書いた参照とバイト列が違います");

        // 基準を作った環境と同じコーデックなら、保存してある JPEG ともバイト単位で一致する
        var baselineBytes = File.ReadAllBytes(ExpectedPath($"{name}.output.jpg"));
        if (referenceBytes.AsSpan().SequenceEqual(baselineBytes))
            _output.WriteLine($"{name}: 基準の JPEG とバイト単位で一致");
        else
            _output.WriteLine($"{name}: この環境の JPEG エンコーダーは基準を作った環境と結果が違うため、保存した JPEG との比較は省略");
    }

    private static string ExpectedPath(string fileName) => Path.Combine(BaselineFolder, "expected", fileName);

    private static BitmapSource Decode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    /// <summary>ImageCropper.Crop と同じ形（96DPI・Bgr24）で、指定した範囲の画素を写した画像を作る</summary>
    private static BitmapSource CopyRegion(BitmapSource source, int x, int y, int width, int height)
    {
        int stride = width * 3;
        var pixels = new byte[stride * height];
        ToBgr24(source).CopyPixels(new Int32Rect(x, y, width, height), pixels, stride, 0);
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static byte[] EncodeJpeg(BitmapSource image, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(image, null, null, null));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource ToBgr24(BitmapSource image)
        => image.Format == PixelFormats.Bgr24 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);

    private static void AssertSamePixels(BitmapSource expected, BitmapSource actual, string stage)
    {
        Assert.Equal((expected.PixelWidth, expected.PixelHeight), (actual.PixelWidth, actual.PixelHeight));

        int stride = expected.PixelWidth * 3;
        var e = new byte[(long)stride * expected.PixelHeight];
        var a = new byte[e.LongLength];
        ToBgr24(expected).CopyPixels(e, stride, 0);
        ToBgr24(actual).CopyPixels(a, stride, 0);

        long differences = 0;
        int maxDifference = 0;
        for (long i = 0; i < e.LongLength; i++)
        {
            int d = Math.Abs(e[i] - a[i]);
            if (d == 0) continue;
            differences++;
            maxDifference = Math.Max(maxDifference, d);
        }
        Assert.True(differences == 0, $"{stage}: {differences} 個の値が基準と違います（差の最大 {maxDifference}）");
    }
}
