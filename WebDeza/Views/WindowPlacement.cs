using System.Windows;
using WebDeza.Models;

namespace WebDeza.Views;

/// <summary>ウィンドウの位置・大きさ・最大化の保存と復元の計算（座標はすべて DIP）</summary>
public static class WindowPlacement
{
    /// <summary>
    /// 復元する位置と大きさ。保存されていない、または画面（モニターの構成が変わったときなど）の外に出るときは null（既定の位置のまま）。
    /// 大きさは最小サイズより小さくしない
    /// </summary>
    /// <param name="settings">設定</param>
    /// <param name="virtualScreen">すべてのモニターを合わせた範囲</param>
    /// <param name="minWidth">ウィンドウの最小の幅</param>
    /// <param name="minHeight">ウィンドウの最小の高さ</param>
    public static Rect? GetRestoreBounds(AppSettings settings, Rect virtualScreen, double minWidth, double minHeight)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.WindowLeft is not { } left || settings.WindowTop is not { } top
            || settings.WindowWidth is not { } width || settings.WindowHeight is not { } height)
            return null;

        var bounds = new Rect(left, top, Math.Max(minWidth, width), Math.Max(minHeight, height));

        // タイトルバーの左寄りの点が画面の中にないと、ウィンドウをつかんで動かせないので、既定の位置にする
        if (!virtualScreen.IntersectsWith(bounds) || !virtualScreen.Contains(new Point(left + 50, top + 10)))
            return null;
        return bounds;
    }

    /// <summary>閉じるときの位置・大きさ・最大化を設定に書く。最大化・最小化しているときは、元に戻したときの位置と大きさを書く</summary>
    /// <param name="settings">設定</param>
    /// <param name="state">ウィンドウの状態</param>
    /// <param name="normalBounds">通常の状態での位置と大きさ（Window.RestoreBounds、または通常の状態の Left・Top・Width・Height）</param>
    public static void Save(AppSettings settings, WindowState state, Rect normalBounds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!normalBounds.IsEmpty && normalBounds.Width > 0 && normalBounds.Height > 0)
        {
            settings.WindowLeft = normalBounds.Left;
            settings.WindowTop = normalBounds.Top;
            settings.WindowWidth = normalBounds.Width;
            settings.WindowHeight = normalBounds.Height;
        }
        settings.WindowMaximized = state == WindowState.Maximized;
    }
}
