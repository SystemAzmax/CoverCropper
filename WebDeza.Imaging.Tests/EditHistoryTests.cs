using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Geometry;

namespace WebDeza.Imaging.Tests;

public class EditHistoryTests
{
    /// <summary>時刻を手で進める TimeProvider</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks = TimeSpan.FromHours(1).Ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan span) => _ticks += span.Ticks;
    }

    private readonly ManualTimeProvider _time = new();

    /// <summary>枠の X だけが違うレシピ（見分けやすいように）</summary>
    private static EditRecipe At(int x) => EditRecipe.Initial with { Crop = new PixelRect(x, 0, 100, 100) };

    private EditHistory Create(int capacity = 100) => new(EditRecipe.Initial, _time, capacity);

    // ---------------------------------------------------------------- E23: Commit → Undo → Redo

    [Fact]
    public void CommitUndoRedo_ReturnExpectedRecipes()
    {
        var history = Create();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);

        history.Commit(At(1));
        history.Commit(At(2));
        history.Commit(At(3));
        Assert.Equal(At(3), history.Current);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        Assert.Equal(At(2), history.Undo());
        Assert.Equal(At(1), history.Undo());
        Assert.Equal(At(1), history.Current);
        Assert.True(history.CanUndo);
        Assert.True(history.CanRedo);

        Assert.Equal(At(2), history.Redo());
        Assert.Equal(At(2), history.Current);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void UndoToInitial_ThenNoMoreUndo()
    {
        var history = Create();
        history.Commit(At(1));

        Assert.Equal(EditRecipe.Initial, history.Undo());
        Assert.False(history.CanUndo);
        Assert.Throws<InvalidOperationException>(() => history.Undo());
        Assert.Equal(EditRecipe.Initial, history.Current); // 失敗しても状態は変わらない
    }

    [Fact]
    public void Redo_WithoutUndo_Throws()
    {
        var history = Create();
        history.Commit(At(1));

        Assert.Throws<InvalidOperationException>(() => history.Redo());
        Assert.Equal(At(1), history.Current);
    }

    // ---------------------------------------------------------------- E23: Undo 後の Commit

    [Fact]
    public void CommitAfterUndo_DiscardsRedo()
    {
        var history = Create();
        history.Commit(At(1));
        history.Commit(At(2));
        history.Undo();

        history.Commit(At(9));

        Assert.False(history.CanRedo);
        Assert.Equal(At(9), history.Current);
        Assert.Equal(At(1), history.Undo());
        Assert.Equal(EditRecipe.Initial, history.Undo());
    }

    // ---------------------------------------------------------------- E23: 上限

    [Fact]
    public void Commit101Times_DropsOldest()
    {
        var history = Create();

        for (int i = 1; i <= 101; i++) history.Commit(At(i));

        // 100 段まで戻せる。最古（初期状態）は消えていて、戻れるのは 1 回目の Commit まで
        int undoCount = 0;
        while (history.CanUndo)
        {
            history.Undo();
            undoCount++;
        }
        Assert.Equal(100, undoCount);
        Assert.Equal(At(1), history.Current);

        // やり直しで最後まで戻れる
        while (history.CanRedo) history.Redo();
        Assert.Equal(At(101), history.Current);
    }

    [Fact]
    public void SmallCapacity_KeepsOnlyThatManySteps()
    {
        var history = Create(capacity: 2);

        history.Commit(At(1));
        history.Commit(At(2));
        history.Commit(At(3));

        Assert.Equal(At(2), history.Undo());
        Assert.Equal(At(1), history.Undo());
        Assert.False(history.CanUndo);
    }

    // ---------------------------------------------------------------- E23: まとめる

    [Fact]
    public void SameKeyWithinOneSecond_IsCoalescedIntoOneStep()
    {
        var history = Create();
        history.Commit(At(1));

        history.Commit(At(2), "move");
        _time.Advance(TimeSpan.FromMilliseconds(500));
        history.Commit(At(3), "move");

        Assert.Equal(At(3), history.Current);
        Assert.Equal(At(1), history.Undo()); // 1 段にまとまっている
    }

    [Fact]
    public void SameKeyContinuingEachWithinOneSecond_KeepsCoalescing()
    {
        // 矢印キーを押し続けたとき: 最初からは 1 秒を超えても、直前から 1 秒以内なら続けてまとめる
        var history = Create();

        for (int x = 1; x <= 5; x++)
        {
            history.Commit(At(x), "move");
            _time.Advance(TimeSpan.FromMilliseconds(900));
        }

        Assert.Equal(At(5), history.Current);
        Assert.Equal(EditRecipe.Initial, history.Undo());
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void ExactlyOneSecond_IsStillCoalesced_ButLongerIsNot()
    {
        var history = Create();
        history.Commit(At(1), "move");
        _time.Advance(TimeSpan.FromSeconds(1));
        history.Commit(At(2), "move"); // ちょうど 1 秒: まとめる
        _time.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        history.Commit(At(3), "move"); // 1 秒を超えた: 別の段

        Assert.Equal(At(2), history.Undo());
        Assert.Equal(EditRecipe.Initial, history.Undo());
    }

    [Fact]
    public void DifferentKeyOrNoKey_IsNotCoalesced()
    {
        var history = Create();
        history.Commit(At(1), "move");
        history.Commit(At(2), "resize");   // 違うキー
        history.Commit(At(3));             // キーなし
        history.Commit(At(4));             // キーなし同士もまとめない

        Assert.Equal(At(3), history.Undo());
        Assert.Equal(At(2), history.Undo());
        Assert.Equal(At(1), history.Undo());
    }

    [Fact]
    public void UndoOrRedo_StopsCoalescing()
    {
        var history = Create();
        history.Commit(At(1), "move");
        history.Commit(At(2), "move");
        history.Undo();                    // 初期状態へ
        history.Redo();                    // At(2) へ

        history.Commit(At(3), "move");     // 1 秒以内・同じキーでも、元に戻す / やり直しの後はまとめない

        Assert.Equal(At(2), history.Undo());
    }

    [Fact]
    public void CoalescedBackToPreviousState_RemovesStep()
    {
        // 右へ 1 → 左へ 1 のように、まとめた結果が元と同じになったら、段ごと消す
        var history = Create();
        history.Commit(At(1));
        history.Commit(At(2), "move");
        history.Commit(At(1), "move");

        Assert.Equal(At(1), history.Current);
        Assert.Equal(EditRecipe.Initial, history.Undo());
    }

    [Fact]
    public void AfterStepRemovedByCoalescing_NextCommitStartsNewStep()
    {
        // 段を消した後に同じキーで続けても、その前の段（キーの無い At(1)）を置き換えない
        var history = Create();
        history.Commit(At(1));
        history.Commit(At(2), "move");
        history.Commit(At(1), "move");     // 段が消える
        history.Commit(At(3), "move");     // 新しい段

        Assert.Equal(At(3), history.Current);
        Assert.Equal(At(1), history.Undo());
        Assert.Equal(EditRecipe.Initial, history.Undo());
    }

    // ---------------------------------------------------------------- そのほか

    [Fact]
    public void CommitSameAsCurrent_DoesNothing()
    {
        // ドラッグして元の位置で離したときなど
        var history = Create();
        history.Commit(At(1));
        history.Undo();

        history.Commit(EditRecipe.Initial);

        Assert.True(history.CanRedo); // やり直す先も消えない
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Commit_ComparesRecipesByContent()
    {
        // 中身が同じ別のレシピは「変わっていない」
        var history = Create();
        var rotated = EditRecipe.Initial with { Orientation = new ImageOrientation(90, false) };
        history.Commit(rotated);

        history.Commit(EditRecipe.Initial with { Orientation = new ImageOrientation(90, false) });

        Assert.Equal(EditRecipe.Initial, history.Undo());
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new EditHistory(null!, _time));
        Assert.Throws<ArgumentNullException>(() => new EditHistory(EditRecipe.Initial, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditHistory(EditRecipe.Initial, _time, 0));
        Assert.Equal(EditRecipe.Initial, new EditHistory(EditRecipe.Initial, TimeProvider.System).Current);
    }
}
