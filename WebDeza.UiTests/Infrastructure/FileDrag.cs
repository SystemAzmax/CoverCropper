using FlaUI.Core.Input;
using WinForms = System.Windows.Forms;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>ファイルの D&D の結果</summary>
public sealed class FileDragResult
{
    /// <summary>落とした先が返した効果（受け付けなければ None。取り消したときも None）</summary>
    public WinForms.DragDropEffects Effect { get; set; }

    /// <summary>目的の位置にあるあいだのカーソル（受け付けるときはコピー、受け付けないときは禁止の形）</summary>
    public IntPtr CursorWhileOver { get; set; }

    /// <summary>目的の位置にあるあいだに取り込んだ絵（whileOver を渡したとき）</summary>
    public Pixels? ShotWhileOver { get; set; }
}

/// <summary>
/// 本物の OLE のファイルの D&D（設計書 6.5）。小さな WinForms のフォームを出し、そこでマウスを押して DoDragDrop を始め、
/// 目的の位置まで動かして離す（エクスプローラーからドラッグするのと同じ）。
/// マウスの通る点は、ボタンを押す前にすべて歯止め（ScreenGuard）で確かめる。取り消しは Esc ではなく、
/// ドラッグ元の QueryContinueDrag で行う（キーをほかのアプリに送らないため）
/// </summary>
public static class FileDrag
{
    private const int Steps = 25;

    // ドラッグ元のフォームを置く位置（目的の位置からのずれ）。ほかのアプリの最前面のウィンドウに隠れたら、次の位置で試す
    private static readonly (int Dx, int Dy)[] SourceOffsets = [(-400, 150), (250, 150), (-400, -250), (250, -250), (-150, 250)];

    // 目的の位置で少し揺らす（DragOver を確実に起こす）
    private static readonly int[] Wiggle = [3, -3, 3, -3, 3, -3];

    /// <summary>
    /// paths をドラッグして target まで動かす。drop が false なら、落とさずに取り消す。
    /// whileOver は、カーソルが目的の位置にあるあいだに呼ぶ（画面の取り込みなど）
    /// </summary>
    public static FileDragResult Run(IReadOnlyList<string> paths, Point target, bool drop, Func<Pixels>? whileOver = null)
    {
        Dpi.EnsureThread();
        var monitors = ScreenGuard.Monitors();
        ScreenGuard.EnsureSafe(target, monitors);
        foreach (var dx in Wiggle) ScreenGuard.EnsureSafe(new Point(target.X + dx, target.Y), monitors);

        SourceHiddenException? hidden = null;
        foreach (var (dx, dy) in SourceOffsets)
        {
            var start = new Point(target.X + dx, target.Y + dy);
            if (!ScreenGuard.IsSafe(start, monitors)) continue;
            try
            {
                return RunFrom(paths, start, target, drop, whileOver, monitors);
            }
            catch (SourceHiddenException ex)
            {
                hidden = ex;
            }
        }
        throw (Exception?)hidden ?? new UnsafePointerPositionException($"ドラッグ元を置ける位置が無い（目的の位置 ({target.X}, {target.Y})）");
    }

    private static FileDragResult RunFrom(IReadOnlyList<string> paths, Point start, Point target, bool drop,
        Func<Pixels>? whileOver, IReadOnlyList<Rectangle> monitors)
    {
        using var source = new DragSource(paths, start);
        var from = source.Center;
        var path = Enumerable.Range(1, Steps).Select(i => new Point(from.X + (target.X - from.X) * i / Steps, from.Y + (target.Y - from.Y) * i / Steps)).ToList();
        ScreenGuard.EnsureSafe(from, monitors);
        foreach (var p in path) ScreenGuard.EnsureSafe(p, monitors);
        source.EnsureUnderCursorPoint();

        var result = new FileDragResult();
        Pointer.MoveTo(from);
        Mouse.Down(MouseButton.Left);
        bool released = false;
        try
        {
            Thread.Sleep(200);
            foreach (var p in path)
            {
                Mouse.Position = p;
                Thread.Sleep(30);
            }
            foreach (var dx in Wiggle)
            {
                Mouse.Position = new Point(target.X + dx, target.Y);
                Thread.Sleep(80);
            }
            Mouse.Position = target;
            Thread.Sleep(400);

            result.CursorWhileOver = Pointer.CurrentCursor();
            if (whileOver is not null) result.ShotWhileOver = whileOver();

            if (!drop)
            {
                // 取り消しを頼み、OLE が QueryContinueDrag を呼ぶよう、マウスを少し動かす
                source.RequestCancel();
                for (int i = 0; i < 20 && !source.Finished; i++)
                {
                    Mouse.Position = new Point(target.X + Wiggle[i % 2], target.Y);
                    Thread.Sleep(100);
                }
                if (!source.Finished) throw new InvalidOperationException("ドラッグを取り消せない");
            }
            Mouse.Up(MouseButton.Left);
            released = true;
            if (!source.WaitFinished(TimeSpan.FromSeconds(30))) throw new TimeoutException("ドラッグが終わらない");
            result.Effect = source.Effect;
            Thread.Sleep(300);
            return result;
        }
        finally
        {
            if (!released) Mouse.Up(MouseButton.Left);
        }
    }

    /// <summary>ドラッグ元のフォームが、ほかのウィンドウに隠れていた</summary>
    private sealed class SourceHiddenException(string message) : InvalidOperationException(message);

    /// <summary>ドラッグ元のフォーム。専用の STA のスレッドで動かす（DoDragDrop は終わるまで戻らないため）</summary>
    private sealed class DragSource : IDisposable
    {
        private static readonly Size FormSize = new(160, 80);

        private readonly ManualResetEventSlim ready = new();
        private readonly ManualResetEventSlim finished = new();
        private readonly Thread thread;
        private WinForms.Form? form;
        private volatile bool cancelRequested;

        public DragSource(IReadOnlyList<string> paths, Point center)
        {
            string[] files = [.. paths];
            thread = new Thread(() =>
            {
                Dpi.EnsureThread();
                WinForms.Application.OleRequired();
                form = new WinForms.Form
                {
                    FormBorderStyle = WinForms.FormBorderStyle.FixedToolWindow,
                    StartPosition = WinForms.FormStartPosition.Manual,
                    Location = new Point(center.X - FormSize.Width / 2, center.Y - FormSize.Height / 2),
                    Size = FormSize,
                    TopMost = true,
                    ShowInTaskbar = false,
                    Text = "UI テストのドラッグ元",
                };
                form.Shown += (_, _) => ready.Set();
                form.MouseDown += (_, e) =>
                {
                    if (e.Button != WinForms.MouseButtons.Left) return;
                    var data = new WinForms.DataObject(WinForms.DataFormats.FileDrop, files);
                    Effect = form.DoDragDrop(data, WinForms.DragDropEffects.Copy | WinForms.DragDropEffects.Move | WinForms.DragDropEffects.Link);
                    finished.Set();
                };
                form.QueryContinueDrag += (_, e) =>
                {
                    if (cancelRequested) e.Action = WinForms.DragAction.Cancel;
                };
                WinForms.Application.Run(form);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("ドラッグ元のフォームが出ない");
            Thread.Sleep(300);
            Handle = form!.Invoke(() => form.Handle);
            var client = form.Invoke(() => form.RectangleToScreen(form.ClientRectangle));
            Center = Pointer.Center(client);
        }

        public IntPtr Handle { get; }

        /// <summary>フォームの中（ここでマウスを押す）</summary>
        public Point Center { get; }

        public WinForms.DragDropEffects Effect { get; private set; }

        public bool Finished => finished.IsSet;

        public bool WaitFinished(TimeSpan timeout) => finished.Wait(timeout);

        public void RequestCancel() => cancelRequested = true;

        /// <summary>フォームを手前にし、マウスを押す点の下がこのフォームかを確かめる（隠れていれば SourceHiddenException）</summary>
        public void EnsureUnderCursorPoint()
        {
            IntPtr under = IntPtr.Zero;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                AppDriver.BringToFront(Handle);
                Thread.Sleep(150);
                under = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT { X = Center.X, Y = Center.Y }), Native.GaRoot);
                if (under == Handle) return;
            }
            throw new SourceHiddenException($"ドラッグ元のフォームが、ほかのウィンドウ（{under}）に隠れている");
        }

        public void Dispose()
        {
            // DoDragDrop が終わっていないと Invoke は戻らないので、BeginInvoke で頼むだけにする
            try { form?.BeginInvoke(() => form.Close()); }
            catch (InvalidOperationException) { }
            thread.Join(TimeSpan.FromSeconds(5));
            ready.Dispose();
            finished.Dispose();
        }
    }
}
