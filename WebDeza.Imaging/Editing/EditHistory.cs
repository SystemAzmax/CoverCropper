namespace WebDeza.Imaging.Editing;

/// <summary>
/// 画像ごとの編集内容（レシピ）の履歴。元に戻す / やり直しに使う。UI のスレッドだけから使う前提で、スレッドセーフではない。
/// </summary>
public sealed class EditHistory
{
    /// <summary>同じ種類の操作を 1 段にまとめる間隔</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly int _capacity;

    // 状態の並び。_states[_index] が今の状態で、それより前が元に戻す先、後ろがやり直す先
    private readonly List<EditRecipe> _states = new();
    private int _index;

    // 直前の Commit のまとめ用の情報（元に戻す / やり直しをしたら消す）
    private string? _lastKey;
    private long _lastTimestamp;

    /// <param name="initial">最初の状態</param>
    /// <param name="time">時刻（まとめの判定に使う。テストで差し替える）</param>
    /// <param name="capacity">元に戻せる段数の上限（設計書では画像ごとに 100 段）</param>
    public EditHistory(EditRecipe initial, TimeProvider time, int capacity = 100)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _time = time;
        _capacity = capacity;
        _states.Add(initial);
    }

    /// <summary>今の状態</summary>
    public EditRecipe Current => _states[_index];

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index < _states.Count - 1;

    /// <summary>
    /// 確定した変更を積む。やり直す先は消える。今の状態と同じなら何もしない。
    /// 直前の Commit と同じ coalesceKey（null 以外）で、直前から 1 秒以内なら、直前の 1 段を置き換える
    /// （矢印キーを続けて押したときなどに、1 回の操作としてまとめる）。
    /// 段数が上限を超えたら、いちばん古い段を捨てる。
    /// </summary>
    public void Commit(EditRecipe next, string? coalesceKey = null)
    {
        ArgumentNullException.ThrowIfNull(next);
        long now = _time.GetTimestamp();

        bool coalesce = coalesceKey is not null
            && string.Equals(coalesceKey, _lastKey, StringComparison.Ordinal)
            && _index > 0
            && _time.GetElapsedTime(_lastTimestamp, now) <= CoalesceWindow;

        if (coalesce)
        {
            // 直前の 1 段を置き換える。まとめた結果が、その段の前と同じになったら段ごと消す
            _states.RemoveRange(_index, _states.Count - _index);
            _index--;
            if (next.Equals(Current))
            {
                // 段が無くなったので、まとめは打ち切る（次の Commit で、その前の段を置き換えないように）
                _lastKey = null;
                return;
            }
            _states.Add(next);
            _index++;
            _lastKey = coalesceKey;
            _lastTimestamp = now;
            return;
        }

        if (next.Equals(Current)) return;

        _states.RemoveRange(_index + 1, _states.Count - _index - 1); // やり直す先を消す
        _states.Add(next);
        _index++;

        if (_states.Count > _capacity + 1)
        {
            _states.RemoveAt(0);
            _index--;
        }

        _lastKey = coalesceKey;
        _lastTimestamp = now;
    }

    /// <summary>1 段元に戻し、戻した後の状態を返す</summary>
    /// <exception cref="InvalidOperationException">元に戻せない（CanUndo が false）</exception>
    public EditRecipe Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("元に戻せる編集がありません。");
        _index--;
        _lastKey = null;
        return Current;
    }

    /// <summary>1 段やり直し、やり直した後の状態を返す</summary>
    /// <exception cref="InvalidOperationException">やり直せない（CanRedo が false）</exception>
    public EditRecipe Redo()
    {
        if (!CanRedo) throw new InvalidOperationException("やり直せる編集がありません。");
        _index++;
        _lastKey = null;
        return Current;
    }
}
