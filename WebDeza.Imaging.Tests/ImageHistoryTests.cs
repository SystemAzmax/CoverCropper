using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Operations;

namespace WebDeza.Imaging.Tests;

public class ImageHistoryTests
{
    private static ImageBuffer Image(int width, int height, uint bgra) => PasteTests.Solid(width, height, bgra);

    // ---------------------------------------------------------------- 最初の状態

    [Fact]
    public void Initial_HasNoSteps()
    {
        var initial = Image(4, 3, 1);
        var history = new ImageHistory(initial);

        Assert.Same(initial, history.Current);
        Assert.Equal(0, history.StateId);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.UndoName);
        Assert.Null(history.RedoName);
        Assert.Equal(0, history.StoredBytes);
        Assert.Throws<InvalidOperationException>(() => history.Undo());
        Assert.Throws<InvalidOperationException>(() => history.Redo());
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ImageHistory(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageHistory(Image(1, 1, 0), maxSteps: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageHistory(Image(1, 1, 0), maxBytes: 0));
    }

    // ---------------------------------------------------------------- E37: 元に戻す・やり直し

    [Fact]
    public void PushUndoRedo_ReturnsExpectedImagesAndStateIds()
    {
        var a = Image(4, 3, 1);
        var b = Image(2, 2, 2);
        var c = Image(3, 3, 3);
        var d = Image(5, 1, 4);
        var history = new ImageHistory(a);

        history.Push("切り抜き", b);
        long idB = history.StateId;
        history.Push("右に 90° 回転", c);
        long idC = history.StateId;
        history.Push("画像サイズ", d);
        long idD = history.StateId;

        Assert.Equal(new long[] { 1, 2, 3 }, new[] { idB, idC, idD });
        Assert.Same(d, history.Current);

        // Undo 2 回
        Assert.Same(c, history.Undo());
        Assert.Equal(idC, history.StateId);
        Assert.Same(b, history.Undo());
        Assert.Equal(idB, history.StateId);
        Assert.True(history.CanUndo);
        Assert.True(history.CanRedo);

        // Redo 1 回
        Assert.Same(c, history.Redo());
        Assert.Same(c, history.Current);
        Assert.Equal(idC, history.StateId); // やり直すと、その状態の番号に戻る

        // 最初まで戻すと番号 0
        history.Undo();
        history.Undo();
        Assert.Same(a, history.Current);
        Assert.Equal(0, history.StateId);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void PushAfterUndo_DiscardsRedo()
    {
        var history = new ImageHistory(Image(4, 3, 1));
        history.Push("1", Image(4, 3, 2));
        history.Push("2", Image(4, 3, 3));
        history.Undo();

        var e = Image(4, 3, 9);
        history.Push("3", e);

        Assert.False(history.CanRedo);
        Assert.Null(history.RedoName);
        Assert.Same(e, history.Current);
        Assert.Equal(3, history.StateId); // 捨てた状態の番号（2）は使い回さない
        Assert.Equal(2, history.UndoCount);
    }

    [Fact]
    public void Names_AreShownForUndoAndRedo()
    {
        var history = new ImageHistory(Image(4, 3, 1));
        history.Push("ぼかし", Image(4, 3, 2));
        history.Push("左右反転", Image(4, 3, 3));

        Assert.Equal("左右反転", history.UndoName);
        Assert.Null(history.RedoName);

        history.Undo();
        Assert.Equal("ぼかし", history.UndoName);
        Assert.Equal("左右反転", history.RedoName);
    }

    [Fact]
    public void Push_InvalidArguments_Throw()
    {
        var history = new ImageHistory(Image(4, 3, 1));

        Assert.Throws<ArgumentException>(() => history.Push("", Image(4, 3, 2)));
        Assert.Throws<ArgumentNullException>(() => history.Push("x", null!));
        // 範囲が画像からはみ出す
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Push("x", Image(4, 3, 2), new PixelRect(3, 0, 2, 1)));
        Assert.False(history.CanUndo); // 失敗した Push は何も変えない
        Assert.Equal(0, history.StateId);
    }

    // ---------------------------------------------------------------- E38: 範囲つきの Push

    [Fact]
    public void RegionPush_UndoAndRedo_RestoreAllPixels()
    {
        var initial = TestBuffers.Distinct(40, 30);
        var region = new PixelRect(5, 7, 10, 6);
        var edited = Paste.Apply(initial, Image(10, 6, 0xFF0000FF), region.X, region.Y);
        var history = new ImageHistory(initial);

        history.Push("ぼかし", edited, region);

        Assert.Same(edited, history.Current);
        // 履歴が持つのは、範囲の前後の画素だけ（10 × 6 × 4 バイトが 2 つ）
        Assert.Equal(10 * 6 * 4 * 2, history.StoredBytes);

        var undone = history.Undo();
        TestBuffers.AssertSameImage(initial, undone);
        Assert.Equal(0, history.StateId);

        var redone = history.Redo();
        TestBuffers.AssertSameImage(edited, redone);
        Assert.Equal(1, history.StateId);
    }

    [Fact]
    public void RegionPushes_InSequence_UndoAllTheWay()
    {
        var states = new List<ImageBuffer> { TestBuffers.Distinct(20, 20) };
        var history = new ImageHistory(states[0]);
        var regions = new[] { new PixelRect(0, 0, 5, 5), new PixelRect(3, 3, 10, 2), new PixelRect(15, 0, 5, 20) };

        for (int i = 0; i < regions.Length; i++)
        {
            var r = regions[i];
            var next = Paste.Apply(states[^1], Image(r.Width, r.Height, 0xFF000000u + (uint)i), r.X, r.Y);
            history.Push($"編集 {i}", next, r);
            states.Add(next);
        }

        for (int i = states.Count - 2; i >= 0; i--) TestBuffers.AssertSameImage(states[i], history.Undo());
        for (int i = 1; i < states.Count; i++) TestBuffers.AssertSameImage(states[i], history.Redo());
    }

    [Fact]
    public void RegionPush_WithDifferentSize_StoresWholeImages()
    {
        var initial = Image(10, 10, 1);
        var cropped = Image(4, 4, 2);
        var history = new ImageHistory(initial);

        // 大きさが変わる編集に範囲を渡しても、前後の画像を持つ
        history.Push("切り抜き", cropped, new PixelRect(0, 0, 4, 4));

        Assert.Same(initial, history.Undo());
        Assert.Same(cropped, history.Redo());
    }

    // ---------------------------------------------------------------- E39: 上限

    [Fact]
    public void MaxSteps_DropsOldest()
    {
        var history = new ImageHistory(Image(2, 2, 0)); // 既定は 50 段

        for (uint i = 1; i <= 51; i++) history.Push($"編集 {i}", Image(2, 2, i));

        Assert.Equal(ImageHistory.DefaultMaxSteps, history.UndoCount);
        for (int i = 0; i < 50; i++) history.Undo();
        Assert.False(history.CanUndo);
        // いちばん古い段（最初の画像 → 1）が消え、1 の画像までしか戻らない
        Assert.Equal(1u, TestBuffers.PixelAt(history.Current, 0, 0));
        Assert.Equal(1, history.StateId);
    }

    [Fact]
    public void MaxBytes_DropsOldestButKeepsLastStep()
    {
        const int Bytes = 10 * 10 * 4; // 1 枚 400 バイト
        // 前後の画像を持つ段は、前の画像を 1 枚ずつ持つ（後の画像は次の段の前か、今の画像と同じ配列）
        var history = new ImageHistory(Image(10, 10, 0), maxBytes: Bytes * 2);

        history.Push("1", Image(10, 10, 1));
        history.Push("2", Image(10, 10, 2));
        Assert.Equal(2, history.UndoCount);
        Assert.Equal(Bytes * 2, history.StoredBytes);

        history.Push("3", Image(10, 10, 3)); // 3 枚分になるので、いちばん古い段を捨てる
        Assert.Equal(2, history.UndoCount);
        Assert.Equal(Bytes * 2, history.StoredBytes);

        // 1 段だけで上限を超えても、直前の 1 段は残す
        var small = new ImageHistory(Image(10, 10, 0), maxBytes: 1);
        small.Push("1", Image(10, 10, 1));
        small.Push("2", Image(10, 10, 2));
        Assert.Equal(1, small.UndoCount);
        Assert.Equal(1u, TestBuffers.PixelAt(small.Undo(), 0, 0));
    }

    [Fact]
    public void StoredBytes_CountsSharedArraysOnce()
    {
        var a = Image(10, 10, 0);
        var b = Image(10, 10, 1);
        var history = new ImageHistory(a);

        history.Push("1", b);
        history.Push("2", a); // 前の状態と同じ画像に戻す（同じ配列）

        // a は今の画像なので数えず、b だけを数える
        Assert.Equal(400, history.StoredBytes);
    }
}
