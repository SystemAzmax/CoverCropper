using WebDeza.Imaging;
using WebDeza.Imaging.Editing;

namespace WebDeza.Views.Canvas;

/// <summary>
/// キャンバスと道具が、表示中の画像のレシピを読み書きするための窓口（設計書 5.6。MainViewModel が実装）。
/// 画像があるときだけ使う（EditorCanvas は、描く画像があるときだけ呼ぶ）
/// </summary>
public interface IEditorSession
{
    /// <summary>今のレシピ（ドラッグ中は Preview で渡したもの）</summary>
    EditRecipe Recipe { get; }

    /// <summary>編集画像（向きを適用したフル解像度）の幅。切り抜きの座標系の幅</summary>
    int EditedWidth { get; }

    /// <summary>編集画像の高さ</summary>
    int EditedHeight { get; }

    /// <summary>今の画像・比率の選択・書き出しサイズでの、切り抜きの枠の規則</summary>
    FrameRules CropRules { get; }

    /// <summary>書き出しサイズ（矢印キーの移動量に使う）</summary>
    OutputSize OutputSize { get; }

    /// <summary>ドラッグ中の途中の状態を表示する（履歴に積まない）</summary>
    void Preview(EditRecipe recipe);

    /// <summary>変更を確定する（履歴に 1 段積む。直前と同じ coalesceKey で 1 秒以内なら、直前の段を置き換える）</summary>
    void Commit(EditRecipe recipe, string? coalesceKey = null);

    /// <summary>レシピ・画像・規則のどれかが変わった（キャンバスは描き直す）</summary>
    event EventHandler? Changed;
}

public static class EditorSessionExtensions
{
    /// <summary>
    /// 今の枠（編集画像のピクセル座標）。レシピの枠が null なら初期状態の枠、あれば規則に合わせたもの
    /// （書き出しの RenderPipeline と同じ決め方なので、表示と書き出しの範囲がずれない）
    /// </summary>
    public static PixelRect GetCropFrame(this IEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var rules = session.CropRules;
        return session.Recipe.Crop is { } crop ? CropFrameCalculator.Clamp(crop, rules) : CropFrameCalculator.Initial(rules);
    }
}
