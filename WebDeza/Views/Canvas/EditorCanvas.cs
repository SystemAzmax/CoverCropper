using System.Windows;
using System.Windows.Media;
using WebDeza.Imaging;
using WebDeza.ViewModels;

namespace WebDeza.Views.Canvas;

/// <summary>
/// 画像を描くキャンバス（設計書 F-03・5.6）。市松模様の上に表示用画像を描き、選択範囲があればその縁を点線で描く。
/// </summary>
/// <remarks>
/// 表示用画像は、画像（フル解像度）の大きさとして描く。選択範囲は画像のピクセル座標で、IEditorSession から読む。
/// 倍率と位置は ViewTransform で計算する。今は全体表示だけで、ズーム（8-2）とパン（8-3）はこれから足す。
/// マウスで選択範囲を作る・動かすのは、道具の仕組み（8-4）と「選択」の道具（8-5・8-6）で作る。
/// </remarks>
public sealed class EditorCanvas : FrameworkElement
{
    /// <summary>全体表示のときに、画像の周りに空ける余白（DIP。CoverCropper と同じ）</summary>
    public const double FitMargin = 12;

    /// <summary>市松模様の 1 マスの大きさ（DIP。設計書 F-03）</summary>
    public const double CheckerSize = 8;

    /// <summary>表示用画像の 1 px がこの物理ピクセル数以上になったら、補間をやめて画素をそのまま拡大する（設計書 F-03）</summary>
    public const double NearestNeighborThreshold = 2;

    /// <summary>選択範囲の縁の点線の、線と隙間の長さ（DIP）</summary>
    private const double SelectionDash = 4;

    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image), typeof(DisplayImage), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(IEditorSession), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSessionChanged));

    public static readonly DependencyProperty ResetViewRequestProperty = DependencyProperty.Register(
        nameof(ResetViewRequest), typeof(int), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CheckerLightColorProperty = DependencyProperty.Register(
        nameof(CheckerLightColor), typeof(Color), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender, OnCheckerColorChanged));

    public static readonly DependencyProperty CheckerDarkColorProperty = DependencyProperty.Register(
        nameof(CheckerDarkColor), typeof(Color), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Color.FromRgb(0xCC, 0xCC, 0xCC), FrameworkPropertyMetadataOptions.AffectsRender, OnCheckerColorChanged));

    public static readonly DependencyProperty SelectionLightBrushProperty = DependencyProperty.Register(
        nameof(SelectionLightBrush), typeof(Brush), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, OnPenBrushChanged));

    public static readonly DependencyProperty SelectionDarkBrushProperty = DependencyProperty.Register(
        nameof(SelectionDarkBrush), typeof(Brush), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender, OnPenBrushChanged));

    private Brush? _checkerBrush;
    private (Pen Dark, Pen Light)? _selectionPens;

    public EditorCanvas()
    {
        ClipToBounds = true;
    }

    /// <summary>描く画像。null なら何も描かない</summary>
    public DisplayImage? Image
    {
        get => (DisplayImage?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    /// <summary>画像の窓口（選択範囲を読む）。null なら選択範囲を描かない</summary>
    public IEditorSession? Session
    {
        get => (IEditorSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// この値が変わったら全体表示に戻す（画像の大きさが変わる編集のたびに 1 増える）。
    /// 今は常に全体表示なので描き直すだけ。ズームの状態を持つ 8-2 から意味を持つ
    /// </summary>
    public int ResetViewRequest
    {
        get => (int)GetValue(ResetViewRequestProperty);
        set => SetValue(ResetViewRequestProperty, value);
    }

    /// <summary>市松模様の白いマスの色</summary>
    public Color CheckerLightColor
    {
        get => (Color)GetValue(CheckerLightColorProperty);
        set => SetValue(CheckerLightColorProperty, value);
    }

    /// <summary>市松模様の灰色のマスの色</summary>
    public Color CheckerDarkColor
    {
        get => (Color)GetValue(CheckerDarkColorProperty);
        set => SetValue(CheckerDarkColorProperty, value);
    }

    /// <summary>選択範囲の縁の点線の色（暗い線の上に重ねる）</summary>
    public Brush SelectionLightBrush
    {
        get => (Brush)GetValue(SelectionLightBrushProperty);
        set => SetValue(SelectionLightBrushProperty, value);
    }

    /// <summary>選択範囲の縁の、点線の下に敷く線の色（明るい画像の上でも縁が見えるように）</summary>
    public Brush SelectionDarkBrush
    {
        get => (Brush)GetValue(SelectionDarkBrushProperty);
        set => SetValue(SelectionDarkBrushProperty, value);
    }

    private static void OnCheckerColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EditorCanvas)d)._checkerBrush = null;

    private static void OnPenBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EditorCanvas)d)._selectionPens = null;

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (EditorCanvas)d;
        if (e.OldValue is IEditorSession oldSession) oldSession.Changed -= canvas.OnSessionContentChanged;
        if (e.NewValue is IEditorSession newSession) newSession.Changed += canvas.OnSessionContentChanged;
    }

    private void OnSessionContentChanged(object? sender, EventArgs e) => InvalidateVisual();

    // ---------------------------------------------------------------- 計算（画面を使わずにテストできる）

    /// <summary>
    /// 補間の方法: 表示用画像の 1 px が 2 物理ピクセル未満なら HighQuality、それ以上なら NearestNeighbor（設計書 F-03）
    /// </summary>
    /// <param name="displayedWidth">表示用画像を描く幅（DIP）</param>
    /// <param name="bitmapPixelWidth">表示用画像の幅（px）</param>
    /// <param name="dpiScale">1 DIP あたりの物理ピクセル</param>
    public static BitmapScalingMode GetScalingMode(double displayedWidth, int bitmapPixelWidth, double dpiScale)
        => displayedWidth * dpiScale / bitmapPixelWidth < NearestNeighborThreshold
            ? BitmapScalingMode.HighQuality
            : BitmapScalingMode.NearestNeighbor;

    /// <summary>
    /// 画像のピクセル座標の矩形を、画面の矩形（DIP）にする。辺は物理ピクセルの境目にそろえる
    /// </summary>
    /// <param name="rect">画像のピクセル座標の矩形</param>
    /// <param name="view">表示倍率と位置</param>
    /// <param name="dpiScale">1 DIP あたりの物理ピクセル</param>
    public static Rect ImageRectToView(PixelRect rect, ViewTransform view, double dpiScale)
    {
        double Snap(double value) => Math.Round(value * dpiScale) / dpiScale;
        var topLeft = view.ImageToView(rect.X, rect.Y);
        var bottomRight = view.ImageToView(rect.X + rect.Width, rect.Y + rect.Height);
        double left = Snap(topLeft.X), top = Snap(topLeft.Y);
        double right = Snap(bottomRight.X), bottom = Snap(bottomRight.Y);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    // ---------------------------------------------------------------- 描画

    protected override void OnRender(DrawingContext dc)
    {
        // マウス操作を受けるため、全面にヒットテストできる背景を敷く（背景の色は親が持つ）
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (Image is not { } image) return;
        double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var view = ViewTransform.Fit(RenderSize, image.ImageWidth, image.ImageHeight, dpiScale, FitMargin);
        var rect = view.ImageBounds(image.ImageWidth, image.ImageHeight);

        // 透過のある画像だけ、下に市松模様を描く（模様は画像の左上からそろえる）
        if (!image.IsOpaque)
        {
            dc.PushTransform(new TranslateTransform(rect.X, rect.Y));
            dc.DrawRectangle(GetCheckerBrush(), null, new Rect(0, 0, rect.Width, rect.Height));
            dc.Pop();
        }

        var drawing = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(drawing, GetScalingMode(rect.Width, image.Bitmap.PixelWidth, dpiScale));
        using (var context = drawing.Open())
        {
            context.DrawImage(image.Bitmap, rect);
        }
        dc.DrawDrawing(drawing);

        // 選択範囲の縁（画像の切り替えの途中で、窓口と画像の大きさが一時的に合わないときは描かない）
        if (Session is { Selection: { } selection } session
            && session.ImageWidth == image.ImageWidth && session.ImageHeight == image.ImageHeight)
        {
            RenderSelection(dc, ImageRectToView(selection, view, dpiScale), dpiScale);
        }
    }

    /// <summary>選択範囲の縁: 暗い線の上に、明るい点線を重ねる（どの道具のときも描く。設計書 F-03）</summary>
    private void RenderSelection(DrawingContext dc, Rect selection, double dpiScale)
    {
        var (dark, light) = GetSelectionPens();

        // 線を物理ピクセルにそろえる（1 物理ピクセルの線がにじんで 2 本分にならないように）
        double half = 0.5 / dpiScale;
        var guidelines = new GuidelineSet();
        guidelines.GuidelinesX.Add(selection.Left + half);
        guidelines.GuidelinesX.Add(selection.Right + half);
        guidelines.GuidelinesY.Add(selection.Top + half);
        guidelines.GuidelinesY.Add(selection.Bottom + half);
        dc.PushGuidelineSet(guidelines);
        dc.DrawRectangle(null, dark, selection);
        dc.DrawRectangle(null, light, selection);
        dc.Pop();
    }

    private (Pen Dark, Pen Light) GetSelectionPens()
    {
        if (_selectionPens is { } pens) return pens;
        var dark = new Pen(SelectionDarkBrush, 1);
        var light = new Pen(SelectionLightBrush, 1) { DashStyle = new DashStyle([SelectionDash, SelectionDash], 0) };
        dark.Freeze();
        light.Freeze();
        _selectionPens = (dark, light);
        return _selectionPens.Value;
    }

    /// <summary>市松模様のブラシ（2×2 マスを敷き詰める）</summary>
    private Brush GetCheckerBrush()
    {
        if (_checkerBrush is not null) return _checkerBrush;

        double size = CheckerSize;
        var dark = new GeometryGroup();
        dark.Children.Add(new RectangleGeometry(new Rect(size, 0, size, size)));
        dark.Children.Add(new RectangleGeometry(new Rect(0, size, size, size)));

        var tile = new DrawingGroup();
        tile.Children.Add(new GeometryDrawing(new SolidColorBrush(CheckerLightColor), null,
            new RectangleGeometry(new Rect(0, 0, size * 2, size * 2))));
        tile.Children.Add(new GeometryDrawing(new SolidColorBrush(CheckerDarkColor), null, dark));

        var brush = new DrawingBrush(tile)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, size * 2, size * 2),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, size * 2, size * 2),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);
        brush.Freeze();
        _checkerBrush = brush;
        return brush;
    }
}
