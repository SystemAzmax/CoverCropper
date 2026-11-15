using WebDeza.Imaging.Operations;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Filters;

/// <summary>
/// フィルターを、画像の全体か、矩形の範囲（選択範囲）だけにかける（設計書 F-06）。入力は変えずに、新しい画像を返す。
/// </summary>
public static class RegionFilter
{
    /// <summary>
    /// region が null なら画像全体に、あれば region の範囲だけにフィルターをかける。
    /// 範囲にかけるときは、範囲を切り出してフィルターをかけ、元の位置に貼る（Crop → フィルター → Paste）。
    /// そのため、範囲の外の画素は、フィルターの材料にも結果にも入らない（ぼかしでも、範囲の外の色が混ざらない）。
    /// 値はフィルターの記述に合わせてから渡し、既定値（IsIdentity）なら何もせずに image を返す。
    /// </summary>
    /// <param name="image">今の画像</param>
    /// <param name="region">かける範囲（画像のピクセル座標）。null なら画像全体</param>
    /// <param name="filter">フィルター</param>
    /// <param name="values">フィルターの値</param>
    /// <param name="context">フィルターをかけるときの状況（PixelScale と取り消し）</param>
    /// <exception cref="ArgumentOutOfRangeException">region が画像からはみ出す、または幅か高さが 0 以下</exception>
    /// <exception cref="InvalidOperationException">フィルターが、渡したものと大きさの違う画像を返した</exception>
    public static ImageBuffer Apply(ImageBuffer image, PixelRect? region, IImageFilter filter,
        ParameterValues values, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();

        // 範囲は値より先に確かめる（既定値でも、範囲の誤りは呼び出し側の誤りとして知らせる）
        var target = region is { } rect ? Crop.Apply(image, rect, context.CancellationToken) : image;

        var normalized = values.Normalize(filter.Parameters);
        if (filter.IsIdentity(normalized)) return image;

        var filtered = filter.Apply(target, normalized, context);
        if (filtered is null || filtered.Width != target.Width || filtered.Height != target.Height)
        {
            throw new InvalidOperationException(
                $"フィルター「{filter.Id}」が、{target.Width}×{target.Height} の画像から " +
                $"{filtered?.Width ?? 0}×{filtered?.Height ?? 0} の画像を返しました。フィルターは大きさを変えないこと。");
        }

        return region is { } r ? Paste.Apply(image, filtered, r.X, r.Y, context.CancellationToken) : filtered;
    }
}
