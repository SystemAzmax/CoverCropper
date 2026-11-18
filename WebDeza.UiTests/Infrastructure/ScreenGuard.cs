using FlaUI.Core.Input;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>マウスを画面の端に近い所へ動かそうとした</summary>
public sealed class UnsafePointerPositionException(string message) : InvalidOperationException(message);

/// <summary>
/// マウスの歯止め。画面の端から 200 px 以内には動かさない（設計書 6.5）。
/// 利用者の PC は KVM ソフト（Multiplicity）を使っていて、画面の端にマウスが行くと、操作が別の PC に移る
/// </summary>
public static class ScreenGuard
{
    /// <summary>画面の端から離す距離（物理ピクセル）</summary>
    public const int Margin = 200;

    /// <summary>
    /// その点の周り（上下左右に margin）がすべて、どれかのモニターの上にあるか。
    /// モニターの並びは長方形とは限らない（モニターの無い所との境も、画面の端として扱う）
    /// </summary>
    public static bool IsSafe(Point point, IReadOnlyList<Rectangle> monitors, int margin = Margin)
    {
        var square = new Rectangle(point.X - margin, point.Y - margin, margin * 2 + 1, margin * 2 + 1);
        long covered = 0;
        foreach (var monitor in monitors)
        {
            var part = Rectangle.Intersect(square, monitor);
            if (!part.IsEmpty) covered += (long)part.Width * part.Height;
        }
        // モニターは重ならないので、重なった面積の合計が正方形の面積と同じなら、すべて覆われている
        return covered == (long)square.Width * square.Height;
    }

    /// <summary>安全でなければ例外にする</summary>
    public static void EnsureSafe(Point point, IReadOnlyList<Rectangle> monitors)
    {
        if (!IsSafe(point, monitors))
            throw new UnsafePointerPositionException(
                $"マウスを ({point.X}, {point.Y}) に動かそうとしたので止めた（画面の端から {Margin} px 以内。モニター: {string.Join(" ", monitors)}）");
    }

    /// <summary>今のモニターの範囲（物理ピクセル）</summary>
    public static IReadOnlyList<Rectangle> Monitors()
    {
        Dpi.EnsureThread();
        var list = new List<Rectangle>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref Native.RECT r, IntPtr _) =>
        {
            list.Add(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}

/// <summary>マウスの操作。すべて歯止め（ScreenGuard）を通す。動かすときは、途中を通らずにその点へ置く</summary>
public static class Pointer
{
    public static void MoveTo(Point point)
    {
        ScreenGuard.EnsureSafe(point, ScreenGuard.Monitors());
        Mouse.Position = point;
        Thread.Sleep(60);
    }

    public static void Click(Point point)
    {
        MoveTo(point);
        Mouse.Click(MouseButton.Left);
        Thread.Sleep(150);
    }

    public static void DoubleClick(Point point)
    {
        MoveTo(point);
        Mouse.DoubleClick(MouseButton.Left);
        Thread.Sleep(300);
    }

    /// <summary>ホイール（正で上、負で下に、notches 段）</summary>
    public static void Wheel(Point point, int notches)
    {
        MoveTo(point);
        Mouse.Scroll(notches);
        Thread.Sleep(200);
    }

    /// <summary>左ボタンで、from から to まで steps 回に分けてドラッグする。途中の点もすべて歯止めを通す</summary>
    public static void Drag(Point from, Point to, int steps = 15)
    {
        var monitors = ScreenGuard.Monitors();
        for (int i = 0; i <= steps; i++) ScreenGuard.EnsureSafe(Lerp(from, to, i, steps), monitors);

        MoveTo(from);
        Mouse.Down(MouseButton.Left);
        try
        {
            Thread.Sleep(80);
            for (int i = 1; i <= steps; i++)
            {
                Mouse.Position = Lerp(from, to, i, steps);
                Thread.Sleep(25);
            }
            Thread.Sleep(80);
        }
        finally
        {
            Mouse.Up(MouseButton.Left);
        }
        Thread.Sleep(300);
    }

    /// <summary>要素の中心</summary>
    public static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static readonly IntPtr WaitCursor = Native.LoadCursor(IntPtr.Zero, 32514);
    private static readonly IntPtr AppStartingCursor = Native.LoadCursor(IntPtr.Zero, 32650);

    /// <summary>今のカーソルの形</summary>
    public static IntPtr CurrentCursor()
    {
        var info = new Native.CURSORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.CURSORINFO>() };
        Native.GetCursorInfo(ref info);
        return info.Cursor;
    }

    /// <summary>待機カーソル（砂時計か、矢印つきの砂時計）か</summary>
    public static bool IsBusyCursor(IntPtr cursor) => cursor == WaitCursor || cursor == AppStartingCursor;

    private static Point Lerp(Point a, Point b, int i, int n) =>
        new(a.X + (b.X - a.X) * i / n, a.Y + (b.Y - a.Y) * i / n);
}
