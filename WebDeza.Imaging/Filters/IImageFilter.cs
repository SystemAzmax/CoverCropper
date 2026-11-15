using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Filters;

/// <summary>
/// 編集機能（フィルター）。FilterRegistry に登録すると、メニューの「フィルター」と編集パネルに自動で並ぶ。
/// 画像の全体か選択範囲に、RegionFilter を通してかける。
/// パラメーターは ParameterDescriptor で記述し、画面の入力欄はそこから自動で作る。
/// </summary>
public interface IImageFilter
{
    /// <summary>"blur" など。設定などに書くので変えない</summary>
    string Id { get; }

    /// <summary>"ぼかし" など。メニューと編集パネルに出す</summary>
    string DisplayName { get; }

    IReadOnlyList<ParameterDescriptor> Parameters { get; }

    /// <summary>値が既定値で、画像を変えないときに true。true なら Apply は呼ばれない</summary>
    bool IsIdentity(ParameterValues values);

    /// <summary>フィルターをかけた画像を返す。入力は変えず、大きさも変えない（範囲にかけるとき、元の位置に貼り戻すため）</summary>
    ImageBuffer Apply(ImageBuffer source, ParameterValues values, FilterContext context);
}

/// <summary>フィルターをかけるときの状況</summary>
/// <param name="PixelScale">
/// この画像の 1 px が、フル解像度の画像の何 px に当たるか（確定するときは 1。縮小した表示用画像でのプレビューでは 1 より大きい）。
/// 半径などの長さを持つフィルターは、長さを PixelScale で割って使い、プレビューと確定後の見た目をそろえる
/// </param>
/// <param name="CancellationToken">取り消し</param>
public sealed record FilterContext(double PixelScale, CancellationToken CancellationToken)
{
    /// <summary>フル解像度の画像にかけるとき（確定・書き出し。PixelScale = 1）</summary>
    public static FilterContext ForExport(CancellationToken cancellationToken = default) => new(1, cancellationToken);
}
