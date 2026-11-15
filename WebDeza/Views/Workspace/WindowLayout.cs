using System.Windows;

namespace WebDeza.Views.Workspace;

/// <summary>子ウィンドウの縁のうち、大きさを変えるためにつかんだ所</summary>
[Flags]
public enum ResizeEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    TopLeft = Top | Left,
    TopRight = Top | Right,
    BottomLeft = Bottom | Left,
    BottomRight = Bottom | Right,
}

/// <summary>
/// ワークスペースの中の子ウィンドウの配置の計算（設計書 F-02・5.6）。画面を使わずにテストできるよう、純粋関数にしてある。
/// 座標はワークスペースの左上を原点とする DIP。
/// </summary>
/// <remarks>
/// 子ウィンドウは、タイトルバーがワークスペースの外に出ないようにする（タイトルバーは子ウィンドウの上端にあり、幅いっぱい）。
/// </remarks>
public static class WindowLayout
{
    /// <summary>子ウィンドウの最小の幅（DIP。設計書 F-02）</summary>
    public const double MinWidth = 160;

    /// <summary>子ウィンドウの最小の高さ（DIP。設計書 F-02）</summary>
    public const double MinHeight = 120;

    /// <summary>新しく開いた子ウィンドウの大きさの上限（ワークスペースに対する割合。設計書 F-02）</summary>
    public const double NewWindowMaxRatio = 0.8;

    /// <summary>
    /// 新しく開いた子ウィンドウの大きさ: 画像が全体表示で収まる大きさ。ただし、ワークスペースの 80% まで（縦横比は保つ）。
    /// 全体表示は 100%（画像の 1 px = 1 物理ピクセル）を超えて拡大しないので、小さい画像は 100% で収まる大きさになる
    /// </summary>
    /// <param name="workspace">ワークスペースの大きさ</param>
    /// <param name="imageWidth">画像の幅（px）</param>
    /// <param name="imageHeight">画像の高さ（px）</param>
    /// <param name="dpiScale">1 DIP あたりの物理ピクセル</param>
    /// <param name="chrome">子ウィンドウのうち、画像を描かない部分の幅と高さ（枠・タイトルバー・キャンバスの余白）</param>
    public static Size NewWindowSize(Size workspace, int imageWidth, int imageHeight, double dpiScale, Size chrome)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiScale);

        double availableWidth = Math.Max(1, workspace.Width * NewWindowMaxRatio - chrome.Width);
        double availableHeight = Math.Max(1, workspace.Height * NewWindowMaxRatio - chrome.Height);
        double scale = Math.Min(1 / dpiScale, Math.Min(availableWidth / imageWidth, availableHeight / imageHeight));

        return new Size(
            Math.Max(MinWidth, CeilingDip(imageWidth * scale + chrome.Width)),
            Math.Max(MinHeight, CeilingDip(imageHeight * scale + chrome.Height)));
    }

    /// <summary>新しく開いた子ウィンドウを、前の子ウィンドウからずらす量（DIP。前のタイトルバーが見えるように、その高さと同じ）</summary>
    public const double CascadeStep = 30;

    /// <summary>
    /// 新しく開いた子ウィンドウの位置（設計書 F-02）: 前に開いた子ウィンドウから右下へ CascadeStep ずらす。
    /// 子ウィンドウがワークスペースに収まらなくなったら（端まで来たら）、左上に戻る
    /// </summary>
    /// <param name="previous">前に開いた子ウィンドウ（無ければ null。左上に置く）</param>
    /// <param name="size">新しい子ウィンドウの大きさ</param>
    /// <param name="workspace">ワークスペースの大きさ</param>
    public static Point NewWindowPosition(Rect? previous, Size size, Size workspace)
    {
        if (previous is not { } rect) return new Point(0, 0);

        var position = new Point(rect.X + CascadeStep, rect.Y + CascadeStep);
        bool fits = position.X + size.Width <= workspace.Width && position.Y + size.Height <= workspace.Height;
        return fits ? position : new Point(0, 0);
    }

    /// <summary>
    /// 重ねて表示（設計書 F-02）: 左上から CascadeStep ずつ右下へずらして置く。大きさは今のまま。
    /// 収まらなくなったら（端まで来たら）、左上に戻る
    /// </summary>
    /// <param name="sizes">子ウィンドウの大きさ（奥から手前の順。手前のものほど右下に来て、奥のタイトルバーが見える）</param>
    /// <param name="workspace">ワークスペースの大きさ</param>
    public static IReadOnlyList<Rect> Cascade(IReadOnlyList<Size> sizes, Size workspace)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        var result = new List<Rect>(sizes.Count);
        Rect? previous = null;
        foreach (var size in sizes)
        {
            var rect = new Rect(NewWindowPosition(previous, size, workspace), size);
            result.Add(rect);
            previous = rect;
        }
        return result;
    }

    /// <summary>並べて表示の格子の列の数と、各段の子ウィンドウの数（上の段から）</summary>
    /// <remarks>列の数は √n の切り上げ、段の数は n ÷ 列の数 の切り上げ。割り切れないときは、上の段に 1 つずつ多く置く</remarks>
    public static IReadOnlyList<int> TileRows(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0) return [];

        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (int)Math.Ceiling((double)count / columns);
        return Enumerable.Range(0, rows).Select(row => count / rows + (row < count % rows ? 1 : 0)).ToList();
    }

    /// <summary>
    /// 並べて表示（設計書 F-02）: ワークスペースを格子に分けて、すべての子ウィンドウが重ならずに見えるように置く。
    /// 子ウィンドウの少ない段は、横に広げて隙間を作らない。辺は DIP の整数にそろえる。
    /// ただし、最小の大きさより小さくはしない（とても多いときは重なる）
    /// </summary>
    /// <param name="count">子ウィンドウの数（並べる順は、呼ぶ側が決める）</param>
    /// <param name="workspace">ワークスペースの大きさ</param>
    public static IReadOnlyList<Rect> Tile(int count, Size workspace)
    {
        var rows = TileRows(count);
        var result = new List<Rect>(count);
        for (int row = 0; row < rows.Count; row++)
        {
            double top = Math.Round(workspace.Height * row / rows.Count);
            double bottom = Math.Round(workspace.Height * (row + 1) / rows.Count);
            int columns = rows[row];
            for (int column = 0; column < columns; column++)
            {
                double left = Math.Round(workspace.Width * column / columns);
                double right = Math.Round(workspace.Width * (column + 1) / columns);
                // 最小の幅に広げたときは、タイトルバーが右へはみ出さないように左へ寄せる（縦は上の辺が格子の中にあればよい）
                double width = Math.Max(MinWidth, right - left);
                double height = Math.Max(MinHeight, bottom - top);
                result.Add(new Rect(Clamp(left, 0, workspace.Width - width), top, width, height));
            }
        }
        return result;
    }

    /// <summary>DIP に切り上げる（計算の誤差でわずかに超えただけのときは切り上げない）</summary>
    private static double CeilingDip(double value) => Math.Ceiling(value - 1e-6);

    /// <summary>タイトルバーが、すべてワークスペースの内側にあるか</summary>
    public static bool IsTitleBarInside(Rect window, Size workspace, double titleBarHeight)
        => window.X >= 0 && window.Y >= 0
           && window.X + window.Width <= workspace.Width
           && window.Y + titleBarHeight <= workspace.Height;

    /// <summary>
    /// ワークスペースの大きさが変わったときの寄せ直し（設計書 F-02）。タイトルバーが外に出ているものだけを、内側へ寄せる。
    /// ワークスペースより幅の広いものは、ワークスペースの幅まで縮める（最小の幅より狭くはしない）
    /// </summary>
    public static Rect KeepInside(Rect window, Size workspace, double titleBarHeight)
    {
        if (IsTitleBarInside(window, workspace, titleBarHeight)) return window;

        double width = Math.Max(MinWidth, Math.Min(window.Width, workspace.Width));
        return new Rect(
            Clamp(window.X, 0, workspace.Width - width),
            Clamp(window.Y, 0, workspace.Height - titleBarHeight),
            width, window.Height);
    }

    /// <summary>
    /// タイトルバーのドラッグでの移動。ドラッグを始めたときの位置に、そこからの移動量を足す。
    /// タイトルバーがワークスペースの外に出ない所で止める
    /// </summary>
    /// <param name="start">ドラッグを始めたときの子ウィンドウ</param>
    /// <param name="delta">ドラッグを始めてからのマウスの移動量</param>
    public static Rect Move(Rect start, Vector delta, Size workspace, double titleBarHeight)
        => new(
            Clamp(start.X + delta.X, 0, workspace.Width - start.Width),
            Clamp(start.Y + delta.Y, 0, workspace.Height - titleBarHeight),
            start.Width, start.Height);

    /// <summary>
    /// 縁・角のドラッグでの大きさの変更。つかんだ辺だけを動かし、反対の辺は動かさない。
    /// 最小の大きさより小さくせず、ワークスペースの外へは広げない。
    /// 上の辺（タイトルバー）は、下の辺がワークスペースの外にはみ出していても、ワークスペースの下に出る所までは動かさない
    /// </summary>
    /// <param name="start">ドラッグを始めたときの子ウィンドウ</param>
    /// <param name="edges">つかんだ辺</param>
    /// <param name="delta">ドラッグを始めてからのマウスの移動量</param>
    public static Rect Resize(Rect start, ResizeEdges edges, Vector delta, Size workspace, double titleBarHeight)
    {
        double left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;

        if (edges.HasFlag(ResizeEdges.Left))
            left = Clamp(start.Left + delta.X, 0, right - MinWidth);
        if (edges.HasFlag(ResizeEdges.Right))
            right = Clamp(start.Right + delta.X, left + MinWidth, Math.Max(left + MinWidth, workspace.Width));
        if (edges.HasFlag(ResizeEdges.Top))
            top = Clamp(start.Top + delta.Y, 0, Math.Min(bottom - MinHeight, workspace.Height - titleBarHeight));
        if (edges.HasFlag(ResizeEdges.Bottom))
            bottom = Clamp(start.Bottom + delta.Y, top + MinHeight, Math.Max(top + MinHeight, workspace.Height));

        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>value を [min, max] に収める。max が min より小さいときは min にする（ワークスペースが子ウィンドウより狭いとき）</summary>
    private static double Clamp(double value, double min, double max)
        => max < min ? min : Math.Min(Math.Max(value, min), max);
}
