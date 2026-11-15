using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Geometry;
using WebDeza.Imaging.Operations;

namespace WebDeza.Imaging.Editing;

/// <summary>
/// 元画像とレシピから、書き出す画像と表示用の画像を作る（非破壊編集）。入力は変えない。
/// </summary>
public sealed class RenderPipeline
{
    private readonly FilterRegistry _filters;

    public RenderPipeline(FilterRegistry filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        _filters = filters;
    }

    /// <summary>
    /// 書き出す画像を作る: 向き → 切り抜き → リサイズ（大きさが同じなら省く） → フィルター（PixelScale = 1）
    /// → 形式が透過を保てず、画像が不透明でなければ背景色で塗る。
    /// </summary>
    /// <param name="source">元画像（向きを直した Bgra32）</param>
    /// <param name="recipe">編集内容。枠が null なら初期状態の枠、あれば規則に合わせて使う</param>
    /// <param name="aspect">切り抜きの比率の選択</param>
    /// <param name="export">書き出し設定（大きさと背景色を使う）</param>
    /// <param name="encoderSupportsAlpha">書き出す形式が透過を保てるか</param>
    /// <param name="cancellationToken">取り消し</param>
    public ImageBuffer RenderForExport(ImageBuffer source, EditRecipe recipe, CropAspect aspect,
        ExportSettings export, bool encoderSupportsAlpha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentNullException.ThrowIfNull(export);
        cancellationToken.ThrowIfCancellationRequested();

        var edited = Orientation.Apply(source, recipe.Orientation, cancellationToken);
        var frame = ResolveFrame(edited.Width, edited.Height, recipe, aspect, export);
        var cropped = Crop.Apply(edited, frame, cancellationToken);

        var (width, height) = CropFrameCalculator.OutputPixelSize(frame, export.Size);
        var resized = Resize.Apply(cropped, width, height, cancellationToken);

        var filtered = ApplyFilters(resized, recipe, new FilterContext(1, cancellationToken));

        return encoderSupportsAlpha ? filtered : Flatten.Apply(filtered, export.Background, cancellationToken);
    }

    /// <summary>
    /// 表示用の画像を作る: 向き → 長辺 maxSide 以下に縮小 → フィルター。
    /// 切り抜きはしない（キャンバスが枠を重ねて描く）。透過も残す（キャンバスが市松模様の上に描く）
    /// </summary>
    /// <param name="source">元画像（向きを直した Bgra32）</param>
    /// <param name="recipe">編集内容</param>
    /// <param name="aspect">切り抜きの比率の選択（フィルターの PixelScale を求めるのに使う）</param>
    /// <param name="export">書き出し設定（フィルターの PixelScale を求めるのに使う）</param>
    /// <param name="maxSide">表示用画像の長辺の上限（設計書では 4096）</param>
    /// <param name="cancellationToken">取り消し</param>
    public PreviewImage RenderPreview(ImageBuffer source, EditRecipe recipe, CropAspect aspect,
        ExportSettings export, int maxSide, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSide);
        cancellationToken.ThrowIfCancellationRequested();

        var edited = Orientation.Apply(source, recipe.Orientation, cancellationToken);
        var (width, height) = FitWithin(edited.Width, edited.Height, maxSide);
        var baseImage = Resize.Apply(edited, width, height, cancellationToken);

        return FilterPreview(baseImage, edited.Width, edited.Height, recipe, aspect, export, cancellationToken);
    }

    /// <summary>
    /// 表示用画像（フィルター前の Base）にフィルターをかけ直す。フィルターの値を変えたときや、
    /// 90° 回転・反転を Base に差分で適用したときに、元画像から作り直さずに済ませるために使う
    /// </summary>
    /// <param name="baseImage">フィルター前の表示用画像（向きは適用済み）</param>
    /// <param name="editedWidth">向きを適用したフル解像度の幅</param>
    /// <param name="editedHeight">向きを適用したフル解像度の高さ</param>
    /// <param name="recipe">編集内容</param>
    /// <param name="aspect">切り抜きの比率の選択</param>
    /// <param name="export">書き出し設定</param>
    /// <param name="cancellationToken">取り消し</param>
    public PreviewImage FilterPreview(ImageBuffer baseImage, int editedWidth, int editedHeight, EditRecipe recipe,
        CropAspect aspect, ExportSettings export, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseImage);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentNullException.ThrowIfNull(export);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(editedWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(editedHeight);
        cancellationToken.ThrowIfCancellationRequested();

        // この画像の 1 px が書き出し画像の何 px に当たるか
        //   = (編集画像の px ÷ 表示用画像の px) × 書き出し倍率（書き出し画像の px ÷ 編集画像の px）
        var frame = ResolveFrame(editedWidth, editedHeight, recipe, aspect, export);
        double pixelScale = (double)editedWidth / baseImage.Width * CropFrameCalculator.OutputScale(frame, export.Size);

        var image = ApplyFilters(baseImage, recipe, new FilterContext(pixelScale, cancellationToken));
        return new PreviewImage(baseImage, image, editedWidth, editedHeight);
    }

    /// <summary>レシピの枠を決める。null なら初期状態、あれば規則に合わせる</summary>
    private static PixelRect ResolveFrame(int editedWidth, int editedHeight, EditRecipe recipe, CropAspect aspect,
        ExportSettings export)
    {
        var rules = CropFrameCalculator.Rules(editedWidth, editedHeight, aspect, export.Size);
        return recipe.Crop is { } crop ? CropFrameCalculator.Clamp(crop, rules) : CropFrameCalculator.Initial(rules);
    }

    /// <summary>
    /// 登録されたフィルターを登録順にかける。値はフィルターの記述に合わせてから渡し、既定値（IsIdentity）のものは省く。
    /// レシピにあっても登録されていないフィルターは使わない
    /// </summary>
    private ImageBuffer ApplyFilters(ImageBuffer image, EditRecipe recipe, FilterContext context)
    {
        foreach (var filter in _filters.All)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var values = recipe.GetFilter(filter.Id).Normalize(filter.Parameters);
            if (filter.IsIdentity(values)) continue;
            image = filter.Apply(image, values, context);
        }
        return image;
    }

    /// <summary>長辺が maxSide 以下になる大きさ（縦横比を保ち、各辺は最低 1）。既に収まっていればそのまま</summary>
    private static (int Width, int Height) FitWithin(int width, int height, int maxSide)
    {
        int longest = Math.Max(width, height);
        if (longest <= maxSide) return (width, height);
        return (Math.Max(1, CropFrameCalculator.RoundDiv((long)width * maxSide, longest)),
                Math.Max(1, CropFrameCalculator.RoundDiv((long)height * maxSide, longest)));
    }
}

/// <summary>表示用の画像</summary>
/// <param name="Base">フィルターをかける前の表示用画像（向きを適用し、長辺を縮小したもの）</param>
/// <param name="Image">フィルターをかけた表示用画像（キャンバスに描くもの）</param>
/// <param name="EditedWidth">向きを適用したフル解像度の幅（切り抜きの座標系の幅）</param>
/// <param name="EditedHeight">向きを適用したフル解像度の高さ</param>
public sealed record PreviewImage(ImageBuffer Base, ImageBuffer Image, int EditedWidth, int EditedHeight);
