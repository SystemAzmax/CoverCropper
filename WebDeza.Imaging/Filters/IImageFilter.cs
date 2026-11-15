using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Filters;

/// <summary>
/// 編集機能（フィルター）。FilterRegistry に登録すると、編集パネルに自動で並び、書き出しにも使われる。
/// パラメーターは ParameterDescriptor で記述し、画面の入力欄はそこから自動で作る。
/// </summary>
public interface IImageFilter
{
    /// <summary>"basic" など。レシピ・プリセット・設定に書くので変えない</summary>
    string Id { get; }

    /// <summary>"基本補正" など。編集パネルのセクションの見出しに使う</summary>
    string DisplayName { get; }

    IReadOnlyList<ParameterDescriptor> Parameters { get; }

    /// <summary>値が既定値で、画像を変えないときに true。true なら Apply は呼ばれない</summary>
    bool IsIdentity(ParameterValues values);

    /// <summary>フィルターをかけた画像を返す。入力は変えない</summary>
    ImageBuffer Apply(ImageBuffer source, ParameterValues values, FilterContext context);
}

/// <summary>フィルターをかけるときの状況</summary>
/// <param name="PixelScale">
/// この画像の 1 px が書き出し画像の何 px に当たるか（書き出しのときは 1）。
/// 半径などを持つフィルターが、表示用の縮小画像でも書き出しと同じ見た目になるように使う
/// </param>
/// <param name="CancellationToken">取り消し</param>
public sealed record FilterContext(double PixelScale, CancellationToken CancellationToken)
{
    /// <summary>書き出し用（PixelScale = 1）</summary>
    public static FilterContext ForExport(CancellationToken cancellationToken = default) => new(1, cancellationToken);
}
