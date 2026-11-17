using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Operations;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

/// <summary>保存する画像を作る（設計書 5.3・5.8。実装計画書 10-1）</summary>
public class ExportRendererTests
{
    private static readonly RgbColor Gray = new(128, 64, 32);

    private static ExportSettings Settings(RgbColor background)
        => new("jpeg", new Dictionary<string, ParameterValues>(), null, background);

    [Theory] // E25
    [InlineData(false)]
    [InlineData(true)]
    public void Render_OpaqueImage_ReturnsInputAsIs(bool encoderSupportsAlpha)
    {
        // 不透明な画像は、透過を保てない形式でも作り直さない
        var image = TestBuffers.DistinctOpaque(20, 10);

        var result = ExportRenderer.Render(image, Settings(Gray), encoderSupportsAlpha);

        Assert.Same(image, result);
    }

    [Fact] // E26: 透過を保てる形式
    public void Render_TransparentImage_ForAlphaFormat_KeepsAlpha()
    {
        var image = TestBuffers.Distinct(20, 10);
        Assert.False(image.IsOpaque);

        var result = ExportRenderer.Render(image, Settings(Gray), encoderSupportsAlpha: true);

        Assert.Same(image, result); // アルファが残る（そのまま渡す）
    }

    [Fact] // E26: 透過を保てない形式
    public void Render_TransparentImage_ForOpaqueFormat_FillsWithBackground()
    {
        var image = TestBuffers.Distinct(20, 10);

        var result = ExportRenderer.Render(image, Settings(Gray), encoderSupportsAlpha: false);

        Assert.True(result.IsOpaque);
        Assert.Equal((20, 10), (result.Width, result.Height)); // 大きさは変えない
        TestBuffers.AssertSameImage(Flatten.Apply(image, Gray), result); // 背景色で塗る
        Assert.NotSame(image.Pixels, result.Pixels); // 入力は変えない
    }

    [Fact]
    public void Render_FullyTransparentPixels_BecomeBackground()
    {
        var pixels = new byte[4 * 4 * 4]; // すべて透明
        var image = ImageBuffer.Create(4, 4, pixels);

        var result = ExportRenderer.Render(image, Settings(Gray), encoderSupportsAlpha: false);

        for (int i = 0; i < result.Pixels.Length; i += 4)
            Assert.Equal(new byte[] { 32, 64, 128, 255 }, result.Pixels[i..(i + 4)]); // B・G・R・A
    }

    [Fact]
    public void Render_Cancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ExportRenderer.Render(TestBuffers.Distinct(4, 4), Settings(Gray), false, cancellation.Token));
    }
}
