using WebDeza.Imaging.Operations;

namespace WebDeza.Imaging.Editing;

/// <summary>
/// 画像 1 枚の履歴（元に戻す / やり直し。設計書 F-07）。編集を確定するたびに、新しい画像を Push する。
/// UI のスレッドだけから使う前提で、スレッドセーフではない。
/// </summary>
/// <remarks>
/// 一部だけが変わる編集（選択範囲へのフィルターなど）は、変わった範囲の前後の画素だけを持つ。
/// それ以外の編集（切り抜き・回転など）は、前後の画像を持つ。
/// </remarks>
public sealed class ImageHistory
{
    /// <summary>段数の上限の既定値</summary>
    public const int DefaultMaxSteps = 50;

    /// <summary>履歴が持つ画素の合計の上限の既定値（512 MB）</summary>
    public const long DefaultMaxBytes = 512L * 1024 * 1024;

    private readonly int _maxSteps;
    private readonly long _maxBytes;

    // 段の並び。_steps[0.._applied - 1] が今の画像までに適用された段（元に戻す先）、それより後ろがやり直す先
    private readonly List<Step> _steps = new();
    private int _applied;
    private long _nextStateId;

    /// <param name="initial">最初の状態（開いた画像）</param>
    /// <param name="maxSteps">元に戻せる段数の上限</param>
    /// <param name="maxBytes">履歴が持つ画素の合計の上限（今の画像は数えない）。超えたら古い段から捨てる（直前の 1 段は残す）</param>
    public ImageHistory(ImageBuffer initial, int maxSteps = DefaultMaxSteps, long maxBytes = DefaultMaxBytes)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSteps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        _maxSteps = maxSteps;
        _maxBytes = maxBytes;
        Current = initial;
        StateId = _nextStateId++;
    }

    /// <summary>今の画像</summary>
    public ImageBuffer Current { get; private set; }

    /// <summary>
    /// 状態ごとの番号。Push で新しい番号になり、Undo / Redo でその状態の番号に戻る
    /// （「書き出した時点の状態かどうか」の判定に使う）。最初の状態は 0
    /// </summary>
    public long StateId { get; private set; }

    public bool CanUndo => _applied > 0;

    public bool CanRedo => _applied < _steps.Count;

    /// <summary>元に戻す操作の名前（無ければ null）</summary>
    public string? UndoName => CanUndo ? _steps[_applied - 1].Name : null;

    /// <summary>やり直す操作の名前（無ければ null）</summary>
    public string? RedoName => CanRedo ? _steps[_applied].Name : null;

    /// <summary>元に戻せる段数（やり直す先は含まない）</summary>
    public int UndoCount => _applied;

    /// <summary>履歴が持つ画素の合計のバイト数（今の画像は数えない。同じ配列は 1 回だけ数える）</summary>
    public long StoredBytes => CountStoredBytes();

    /// <summary>
    /// 確定した変更を積む。やり直す先は消える。
    /// changedRegion があり、next と今の画像の大きさが同じなら、その範囲の前後の画素だけを持つ
    /// （next は、changedRegion の外では今の画像と同じ画素であること）。それ以外は、前後の画像を持つ。
    /// 段数か合計のバイト数が上限を超えたら、古い段から捨てる（直前の 1 段は残す）。
    /// </summary>
    /// <param name="name">操作の名前（メニューの「元に戻す: {名前}」に出す）</param>
    /// <param name="next">変更した後の画像</param>
    /// <param name="changedRegion">変わった範囲（今の画像のピクセル座標）。null なら画像全体が変わったものとして扱う</param>
    /// <exception cref="ArgumentOutOfRangeException">changedRegion が画像からはみ出す</exception>
    public void Push(string name, ImageBuffer next, PixelRect? changedRegion = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(next);

        var before = Current;
        bool sameSize = next.Width == before.Width && next.Height == before.Height;

        Step step;
        if (changedRegion is { } region && sameSize)
        {
            // 範囲が画像からはみ出していれば、Crop が ArgumentOutOfRangeException を投げる（履歴は変えない）
            var beforePatch = Crop.Apply(before, region);
            var afterPatch = Crop.Apply(next, region);
            step = new Step(name, StateId, _nextStateId, null, null, region, beforePatch, afterPatch);
        }
        else
        {
            step = new Step(name, StateId, _nextStateId, before, next, null, null, null);
        }

        _steps.RemoveRange(_applied, _steps.Count - _applied); // やり直す先を消す
        _steps.Add(step);
        _applied = _steps.Count;
        _nextStateId++;

        Current = next;
        StateId = step.AfterId;

        TrimOldSteps();
    }

    /// <summary>
    /// 1 段元に戻し、戻した後の画像を返す。
    /// 戻した画像を作れなかったとき（範囲の画素を貼るときのメモリ不足など）は、履歴を変えずに例外を投げる
    /// </summary>
    /// <exception cref="InvalidOperationException">元に戻せない（CanUndo が false）</exception>
    public ImageBuffer Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("元に戻せる編集がありません。");
        var step = _steps[_applied - 1];
        var image = step.Region is { } region
            ? Paste.Apply(Current, step.BeforePatch!, region.X, region.Y)
            : step.Before!;
        _applied--;
        Current = image;
        StateId = step.BeforeId;
        return Current;
    }

    /// <summary>
    /// 1 段やり直し、やり直した後の画像を返す。
    /// やり直した画像を作れなかったときは、履歴を変えずに例外を投げる
    /// </summary>
    /// <exception cref="InvalidOperationException">やり直せない（CanRedo が false）</exception>
    public ImageBuffer Redo()
    {
        if (!CanRedo) throw new InvalidOperationException("やり直せる編集がありません。");
        var step = _steps[_applied];
        var image = step.Region is { } region
            ? Paste.Apply(Current, step.AfterPatch!, region.X, region.Y)
            : step.After!;
        _applied++;
        Current = image;
        StateId = step.AfterId;
        return Current;
    }

    /// <summary>段数とバイト数の上限を超えた分を、古い段から捨てる（直前の 1 段は残す）</summary>
    private void TrimOldSteps()
    {
        while (_steps.Count > 1 && (_steps.Count > _maxSteps || CountStoredBytes() > _maxBytes))
        {
            _steps.RemoveAt(0);
            _applied--;
        }
    }

    /// <summary>
    /// 履歴が持つ画素の合計。前後の画像は、隣の段や今の画像と同じ配列を共有していることが多いので、
    /// 同じ配列は 1 回だけ数え、今の画像の配列は数えない
    /// </summary>
    private long CountStoredBytes()
    {
        var counted = new HashSet<byte[]>(ReferenceEqualityComparer.Instance) { Current.Pixels };
        long total = 0;
        foreach (var step in _steps)
        {
            foreach (var image in step.StoredImages())
            {
                if (counted.Add(image.Pixels)) total += image.Pixels.Length;
            }
        }
        return total;
    }

    /// <summary>
    /// 1 段分の記録。Region が null なら Before / After（前後の画像）を、あれば BeforePatch / AfterPatch（範囲の前後の画素）を持つ
    /// </summary>
    private sealed record Step(
        string Name, long BeforeId, long AfterId,
        ImageBuffer? Before, ImageBuffer? After,
        PixelRect? Region, ImageBuffer? BeforePatch, ImageBuffer? AfterPatch)
    {
        public IEnumerable<ImageBuffer> StoredImages()
        {
            if (Before is not null) yield return Before;
            if (After is not null) yield return After;
            if (BeforePatch is not null) yield return BeforePatch;
            if (AfterPatch is not null) yield return AfterPatch;
        }
    }
}
