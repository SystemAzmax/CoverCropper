using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.ViewModels;

namespace WebDeza.Views.Canvas;

/// <summary>
/// 中央のキャンバス（設計書 F-03・5.6）。市松模様の上に表示用画像を描き、切り抜きの表示（枠外の暗さ・三分割のガイド・枠線）を重ねる。
/// </summary>
/// <remarks>
/// 表示用画像は、編集画像（フル解像度）の大きさとして描く。枠は編集画像のピクセル座標で、IEditorSession から読む。
/// 今は全体表示だけ。倍率と位置の計算は 6-1 で ViewTransform に移し、ズーム（6-2）とパン（6-3）を足す。
/// 枠の内側のドラッグでの移動は、6-5 で CropTool に移す。
/// </remarks>
public sealed class EditorCanvas : FrameworkElement
{
    /// <summary>全体表示のときに、画像の周りに空ける余白（DIP。CoverCropper と同じ）</summary>
    public const double FitMargin = 12;

    /// <summary>市松模様の 1 マスの大きさ（DIP。設計書 F-03）</summary>
    public const double CheckerSize = 8;

    /// <summary>表示用画像の 1 px がこの物理ピクセル数以上になったら、補間をやめて画素をそのまま拡大する（設計書 F-03）</summary>
    public const double NearestNeighborThreshold = 2;

    /// <summary>枠線の太さ（DIP）。白い線と、その下に敷く暗い線</summary>
    private const double FrameLineThickness = 1;
    private const double FrameShadowThickness = 3;

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

    public static readonly DependencyProperty OutsideFrameBrushProperty = DependencyProperty.Register(
        nameof(OutsideFrameBrush), typeof(Brush), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(FrozenBrush(0x80, 0, 0, 0), FrameworkPropertyMetadataOptions.AffectsRender, OnPenBrushChanged));

    public static readonly DependencyProperty FrameLineBrushProperty = DependencyProperty.Register(
        nameof(FrameLineBrush), typeof(Brush), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, OnPenBrushChanged));

    public static readonly DependencyProperty FrameShadowBrushProperty = DependencyProperty.Register(
        nameof(FrameShadowBrush), typeof(Brush), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(FrozenBrush(0xC0, 0, 0, 0), FrameworkPropertyMetadataOptions.AffectsRender, OnPenBrushChanged));

    public static readonly DependencyProperty GuideLineBrushProperty = DependencyProperty.Register(
        nameof(GuideLineBrush), typeof(Brush), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(FrozenBrush(0x80, 0xFF, 0xFF, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender, OnPenBrushChanged));

    private Brush? _checkerBrush;

    /// <summary>既定値のブラシ（既定値はすべての部品で共有するので、固定しておく）</summary>
    private static SolidColorBrush FrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
    private (Pen Line, Pen Shadow, Pen Guide)? _pens;

    // ドラッグ（枠の移動）の状態
    private bool _dragging;
    private Point _dragStartPoint;
    private PixelRect _dragStartFrame;
    private EditRecipe? _dragStartRecipe;
    private PixelRect _dragLastFrame;
    private double _dragScale;

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

    /// <summary>表示中の画像のレシピの窓口（枠を読み、ドラッグの結果を渡す）。null なら枠を描かない</summary>
    public IEditorSession? Session
    {
        get => (IEditorSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// この値が変わったら全体表示に戻す（ViewModel が画像の切り替えのたびに 1 増やす）。
    /// 今は常に全体表示なので描き直すだけ。ズームの状態を持つ 6-2 から意味を持つ
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

    /// <summary>枠の外を暗くするブラシ（設計書 F-03 では #80000000）</summary>
    public Brush OutsideFrameBrush
    {
        get => (Brush)GetValue(OutsideFrameBrushProperty);
        set => SetValue(OutsideFrameBrushProperty, value);
    }

    /// <summary>枠線のブラシ</summary>
    public Brush FrameLineBrush
    {
        get => (Brush)GetValue(FrameLineBrushProperty);
        set => SetValue(FrameLineBrushProperty, value);
    }

    /// <summary>枠線の下に敷く線のブラシ（明るい画像の上でも枠が見えるように）</summary>
    public Brush FrameShadowBrush
    {
        get => (Brush)GetValue(FrameShadowBrushProperty);
        set => SetValue(FrameShadowBrushProperty, value);
    }

    /// <summary>三分割のガイドのブラシ</summary>
    public Brush GuideLineBrush
    {
        get => (Brush)GetValue(GuideLineBrushProperty);
        set => SetValue(GuideLineBrushProperty, value);
    }

    private static void OnCheckerColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EditorCanvas)d)._checkerBrush = null;

    private static void OnPenBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EditorCanvas)d)._pens = null;

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (EditorCanvas)d;
        if (e.OldValue is IEditorSession oldSession) oldSession.Changed -= canvas.OnSessionContentChanged;
        if (e.NewValue is IEditorSession newSession) newSession.Changed += canvas.OnSessionContentChanged;
    }

    private void OnSessionContentChanged(object? sender, EventArgs e) => InvalidateVisual();

    // ---------------------------------------------------------------- 計算（画面を使わずにテストできる）

    /// <summary>
    /// 全体表示の倍率と位置。倍率は「編集画像の 1 px あたりの DIP」で、画像全体が余白の内側に収まる倍率。
    /// ただし 100%（編集画像の 1 px = 1 物理ピクセル）を超えて拡大しない。
    /// 位置は中央に置き、物理ピクセルの境目にそろえる（にじませないため）
    /// </summary>
    /// <param name="viewport">キャンバスの大きさ（DIP）</param>
    /// <param name="imageWidth">編集画像の幅（px）</param>
    /// <param name="imageHeight">編集画像の高さ（px）</param>
    /// <param name="dpiScale">1 DIP あたりの物理ピクセル（100% の DPI なら 1）</param>
    /// <returns>画像を描く矩形（DIP）と、倍率</returns>
    public static (Rect ImageRect, double Scale) Fit(Size viewport, int imageWidth, int imageHeight, double dpiScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiScale);

        double availableWidth = Math.Max(1, viewport.Width - FitMargin * 2);
        double availableHeight = Math.Max(1, viewport.Height - FitMargin * 2);
        double scale = Math.Min(1 / dpiScale, Math.Min(availableWidth / imageWidth, availableHeight / imageHeight));

        double width = imageWidth * scale;
        double height = imageHeight * scale;
        double x = Math.Round((viewport.Width - width) / 2 * dpiScale) / dpiScale;
        double y = Math.Round((viewport.Height - height) / 2 * dpiScale) / dpiScale;
        return (new Rect(x, y, width, height), scale);
    }

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
    /// 編集画像のピクセル座標の矩形を、画面の矩形（DIP）にする。辺は物理ピクセルの境目にそろえる
    /// </summary>
    /// <param name="frame">編集画像のピクセル座標の矩形</param>
    /// <param name="imageRect">画像を描いている矩形（DIP）</param>
    /// <param name="scale">編集画像の 1 px あたりの DIP</param>
    /// <param name="dpiScale">1 DIP あたりの物理ピクセル</param>
    public static Rect FrameToView(PixelRect frame, Rect imageRect, double scale, double dpiScale)
    {
        double Snap(double value) => Math.Round(value * dpiScale) / dpiScale;
        double left = Snap(imageRect.X + frame.X * scale);
        double top = Snap(imageRect.Y + frame.Y * scale);
        double right = Snap(imageRect.X + (frame.X + frame.Width) * scale);
        double bottom = Snap(imageRect.Y + (frame.Y + frame.Height) * scale);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// 枠の内側のドラッグで動かした後の枠。画面の移動量（DIP）を編集画像のピクセルに換算して（四捨五入）動かし、
    /// 画像の内側に収める。大きさは変えない
    /// </summary>
    /// <param name="start">ドラッグを始めたときの枠</param>
    /// <param name="delta">ドラッグを始めた位置からの、ポインターの移動量（DIP）</param>
    /// <param name="scale">編集画像の 1 px あたりの DIP</param>
    /// <param name="rules">枠の規則</param>
    public static PixelRect DragFrame(PixelRect start, Vector delta, double scale, FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        int dx = (int)Math.Clamp(Math.Round(delta.X / scale, MidpointRounding.AwayFromZero), int.MinValue / 2, int.MaxValue / 2);
        int dy = (int)Math.Clamp(Math.Round(delta.Y / scale, MidpointRounding.AwayFromZero), int.MinValue / 2, int.MaxValue / 2);
        return CropFrameCalculator.Clamp(start with { X = start.X + dx, Y = start.Y + dy }, rules);
    }

    // ---------------------------------------------------------------- 描画

    /// <summary>今の画像の配置（画像が無ければ null）</summary>
    private (DisplayImage Image, Rect ImageRect, double Scale, double DpiScale)? GetLayout()
    {
        if (Image is not { } image) return null;
        double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var (rect, scale) = Fit(RenderSize, image.EditedWidth, image.EditedHeight, dpiScale);
        return (image, rect, scale, dpiScale);
    }

    /// <summary>枠を描く矩形（DIP）。画像か窓口が無い、または大きさが合わなければ null</summary>
    private Rect? GetFrameViewRect((DisplayImage Image, Rect ImageRect, double Scale, double DpiScale) layout)
    {
        if (Session is not { } session) return null;
        // 画像の切り替えの途中で、窓口と画像の大きさが一時的に合わないことがある
        if (session.EditedWidth != layout.Image.EditedWidth || session.EditedHeight != layout.Image.EditedHeight) return null;
        return FrameToView(session.GetCropFrame(), layout.ImageRect, layout.Scale, layout.DpiScale);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // マウス操作を受けるため、全面にヒットテストできる背景を敷く（背景の色は親の Grid が持つ）
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (GetLayout() is not { } layout) return;
        var (image, rect, _, dpiScale) = layout;

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

        if (GetFrameViewRect(layout) is { } frame) RenderFrame(dc, rect, frame, dpiScale);
    }

    /// <summary>切り抜きの表示: 枠外の暗さ・三分割のガイド・枠線（設計書 F-03。どの道具のときも描く）</summary>
    private void RenderFrame(DrawingContext dc, Rect imageRect, Rect frame, double dpiScale)
    {
        var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
        outside.Children.Add(new RectangleGeometry(imageRect));
        outside.Children.Add(new RectangleGeometry(frame));
        dc.DrawGeometry(OutsideFrameBrush, null, outside);

        var (line, shadow, guide) = GetPens();

        // 線を物理ピクセルにそろえる（1 物理ピクセルの線がにじんで 2 本分にならないように）
        double half = 0.5 / dpiScale;
        var guidelines = new GuidelineSet();
        double[] xs = [frame.Left, frame.Left + frame.Width / 3, frame.Left + frame.Width * 2 / 3, frame.Right];
        double[] ys = [frame.Top, frame.Top + frame.Height / 3, frame.Top + frame.Height * 2 / 3, frame.Bottom];
        foreach (var x in xs) guidelines.GuidelinesX.Add(x + half);
        foreach (var y in ys) guidelines.GuidelinesY.Add(y + half);
        dc.PushGuidelineSet(guidelines);

        for (int i = 1; i <= 2; i++)
        {
            dc.DrawLine(guide, new Point(xs[i], frame.Top), new Point(xs[i], frame.Bottom));
            dc.DrawLine(guide, new Point(frame.Left, ys[i]), new Point(frame.Right, ys[i]));
        }
        dc.DrawRectangle(null, shadow, frame);
        dc.DrawRectangle(null, line, frame);

        dc.Pop();
    }

    private (Pen Line, Pen Shadow, Pen Guide) GetPens()
    {
        if (_pens is { } pens) return pens;
        var line = new Pen(FrameLineBrush, FrameLineThickness);
        var shadow = new Pen(FrameShadowBrush, FrameShadowThickness);
        var guide = new Pen(GuideLineBrush, 1) { DashStyle = new DashStyle([4, 4], 0) };
        line.Freeze();
        shadow.Freeze();
        guide.Freeze();
        _pens = (line, shadow, guide);
        return _pens.Value;
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

    // ---------------------------------------------------------------- 枠の移動（ドラッグ）

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (GetLayout() is not { } layout || Session is not { } session) return;
        if (GetFrameViewRect(layout) is not { } frameRect) return;

        var point = e.GetPosition(this);
        if (!frameRect.Contains(point)) return;

        _dragging = true;
        _dragStartPoint = point;
        _dragStartRecipe = session.Recipe;
        _dragStartFrame = session.GetCropFrame();
        _dragLastFrame = _dragStartFrame;
        _dragScale = layout.Scale;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);

        if (_dragging && Session is { } session && _dragStartRecipe is { } startRecipe)
        {
            var frame = DragFrame(_dragStartFrame, point - _dragStartPoint, _dragScale, session.CropRules);
            if (frame != _dragLastFrame)
            {
                _dragLastFrame = frame;
                session.Preview(startRecipe with { Crop = frame }); // ドラッグ中は履歴に積まない
            }
            return;
        }

        Cursor = GetLayout() is { } layout && GetFrameViewRect(layout) is { } frameRect && frameRect.Contains(point)
            ? Cursors.SizeAll
            : null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        EndDrag();
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // ほかのウィンドウにマウスを取られたときも、そこまでの移動を確定する
        if (_dragging) EndDrag();
    }

    /// <summary>ドラッグを終える。枠が動いていれば 1 段だけ履歴に積み、動いていなければ元のレシピに戻す</summary>
    private void EndDrag()
    {
        _dragging = false;
        if (Session is not { } session || _dragStartRecipe is not { } startRecipe) return;
        _dragStartRecipe = null;

        // 動いていなければ元のレシピのまま（枠が null の初期状態を、同じ位置の枠で置き換えて履歴に積まない）
        session.Commit(_dragLastFrame == _dragStartFrame ? startRecipe : startRecipe with { Crop = _dragLastFrame });
    }
}
