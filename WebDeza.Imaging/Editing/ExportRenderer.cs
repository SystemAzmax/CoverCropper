using WebDeza.Imaging.Operations;

namespace WebDeza.Imaging.Editing;

/// <summary>
/// 保存する画像を作る（設計書 5.3・5.8）。保存のダイアログの保存サイズの計算・プレビュー・保存・一括保存で同じものを使う。
/// </summary>
public static class ExportRenderer
{
    /// <summary>
    /// 保存する画像を作る: 形式が透過を保てず（encoderSupportsAlpha が false）、画像が不透明でなければ、
    /// 背景色（export.Background）で塗る（Flatten）。それ以外は image をそのまま返す（作り直さない）。大きさは変えない
    /// </summary>
    /// <param name="image">今の画像（編集を確定した結果の全体）</param>
    /// <param name="export">保存の設定（背景色を使う）</param>
    /// <param name="encoderSupportsAlpha">保存する形式が透過を保てるか（FormatCapabilities.Alpha）</param>
    /// <param name="cancellationToken">取り消し</param>
    public static ImageBuffer Render(ImageBuffer image, ExportSettings export, bool encoderSupportsAlpha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(export);
        cancellationToken.ThrowIfCancellationRequested();

        return encoderSupportsAlpha || image.IsOpaque
            ? image
            : Flatten.Apply(image, export.Background, cancellationToken);
    }
}
