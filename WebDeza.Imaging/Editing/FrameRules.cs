namespace WebDeza.Imaging.Editing;

/// <summary>
/// 切り抜きの枠の規則。編集画像の大きさ・比率の選択・書き出しサイズから CropFrameCalculator.Rules で作る。
/// </summary>
/// <param name="ImageWidth">編集画像の幅</param>
/// <param name="ImageHeight">編集画像の高さ</param>
/// <param name="Aspect">枠の縦横比。null のときは自由</param>
/// <param name="MinWidth">枠の最小の幅</param>
/// <param name="MinHeight">枠の最小の高さ</param>
/// <param name="MaxWidth">枠の最大の幅</param>
/// <param name="MaxHeight">枠の最大の高さ</param>
public sealed record FrameRules(
    int ImageWidth, int ImageHeight, AspectRatio? Aspect,
    int MinWidth, int MinHeight, int MaxWidth, int MaxHeight)
{
    /// <summary>
    /// 縦横比が決まっているときに、幅から高さを求める。
    /// round(幅 × a.H ÷ a.W)（0.5 は 0 から遠い方へ。整数演算）を、1〜画像の高さに収めた値
    /// </summary>
    /// <exception cref="InvalidOperationException">縦横比が決まっていない（自由）</exception>
    public int HeightForWidth(int width)
    {
        if (Aspect is not { } a)
            throw new InvalidOperationException("比率が自由のときは、幅から高さを決められません。");
        return Math.Clamp(CropFrameCalculator.RoundDiv((long)width * a.Height, a.Width), 1, ImageHeight);
    }
}
