using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WebDeza.Imaging;
using WebDeza.ViewModels;

namespace WebDeza.Views.Canvas;

/// <summary>
/// 画像を描くキャンバス（設計書 F-03・5.6）。市松模様の上に表示用画像を描き、選択範囲があればその縁を点線で描く。
/// </summary>
/// <remarks>
/// 表示用画像は、画像（フル解像度）の大きさとして描く。選択範囲は画像のピクセル座標で、IEditorSession から読む。
/// 倍率と位置は ViewTransform で計算する。ズームの状態はキャンバスが持つ（ViewModel には持たせない。設計書 5.6）。
/// 全体表示のときは、大きさが変わるたびに全体表示し直す。ズーム中は、倍率と表示の中心を保つ（設計書 F-03）。
/// パンは、中ボタンのドラッグと Space+ドラッグ（道具にかかわらず。設計書 4.5）と、道具（手のひら）から頼まれたとき。
/// そのほかのマウスとキーは、今の道具（ToolFactory から、このキャンバス用に作った実体）に渡す。ICanvasHost として道具に使わせる。
/// マウスで選択範囲を作る・動かすのは、道具の仕組み（8-4）と「選択」の道具（8-5・8-6）で作る。
/// </remarks>
public sealed class EditorCanvas : FrameworkElement, ICanvasHost
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
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnImageChanged));

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(IEditorSession), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSessionChanged));

    public static readonly DependencyProperty ResetViewRequestProperty = DependencyProperty.Register(
        nameof(ResetViewRequest), typeof(int), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnResetViewRequestChanged));

    public static readonly DependencyProperty ZoomPercentProperty = DependencyProperty.Register(
        nameof(ZoomPercent), typeof(double), typeof(EditorCanvas), new FrameworkPropertyMetadata(0.0));

    public static readonly DependencyProperty CheckerLightColorProperty = DependencyProperty.Register(
        nameof(CheckerLightColor), typeof(Color), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender, OnCheckerColorChanged));

    public static readonly DependencyProperty CheckerDarkColorProperty = DependencyProperty.Register(
        nameof(CheckerDarkColor), typeof(Color), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Color.FromRgb(0xCC, 0xCC, 0xCC), FrameworkPropertyMetadataOptions.AffectsRender, OnCheckerColorChanged));

    public static readonly DependencyProperty ToolFactoryProperty = DependencyProperty.Register(
        nameof(ToolFactory), typeof(ICanvasToolFactory), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((EditorCanvas)d).RecreateTool()));

    public static readonly DependencyProperty SelectionLightBrushProperty = DependencyProperty.Register(
        nameof(SelectionLightBrush), typeof(Brush), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, OnPenBrushChanged));

    public static readonly DependencyProperty SelectionDarkBrushProperty = DependencyProperty.Register(
        nameof(SelectionDarkBrush), typeof(Brush), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender, OnPenBrushChanged));

    private Brush? _checkerBrush;
    private (Pen Dark, Pen Light)? _selectionPens;

    /// <summary>ズーム中の倍率と位置。null なら全体表示（大きさが変わるたびに全体表示し直す）</summary>
    private ViewTransform? _zoomed;

    /// <summary>パン中なら、始めたときのマウスの位置と表示（始めたときからの移動量で計算し、ずれをためない）と、離したら終わるボタン</summary>
    private (Point Start, ViewTransform View, MouseButton Button)? _pan;

    /// <summary>今の道具の、このキャンバス用の実体</summary>
    private ICanvasTool? _tool;

    public EditorCanvas()
    {
        ClipToBounds = true;
        // クリックでフォーカスを取る（入力欄からフォーカスを戻し、キーを道具に渡すため。設計書 4.4）
        Focusable = true;
        FocusVisualStyle = null;
        KeyboardNavigation.SetIsTabStop(this, false);
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

    /// <summary>この値が変わったら全体表示に戻す（画像の大きさが変わる編集のたびに 1 増える。設計書 5.6）</summary>
    public int ResetViewRequest
    {
        get => (int)GetValue(ResetViewRequestProperty);
        set => SetValue(ResetViewRequestProperty, value);
    }

    /// <summary>
    /// 今の表示倍率（%。100% = 画像の 1 px が 1 物理ピクセル）。画像が無ければ 0。
    /// キャンバスが書き、ステータスバーに出すために OneWayToSource で ViewModel に渡す
    /// </summary>
    public double ZoomPercent
    {
        get => (double)GetValue(ZoomPercentProperty);
        set => SetValue(ZoomPercentProperty, value);
    }

    /// <summary>全体表示か（ズームしていない）</summary>
    public bool IsFit => _zoomed is null;

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
        canvas.RecreateTool();
    }

    /// <summary>今の道具（全画像で共通の種類。実体はこのキャンバス用に作る）。null なら道具なし</summary>
    public ICanvasToolFactory? ToolFactory
    {
        get => (ICanvasToolFactory?)GetValue(ToolFactoryProperty);
        set => SetValue(ToolFactoryProperty, value);
    }

    /// <summary>今の道具の実体（テストと確認用）</summary>
    public ICanvasTool? Tool => _tool;

    /// <summary>道具か画像の窓口が変わったら、道具の実体を作り直す（ドラッグ中ならやめる）</summary>
    private void RecreateTool()
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        _tool = ToolFactory is { } factory && Session is { } session ? factory.Create(session, this) : null;
        InvalidateVisual();
    }

    private void OnSessionContentChanged(object? sender, EventArgs e) => InvalidateVisual();

    private static void OnImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 大きさの違う画像になったら、全体表示に戻す
        if (e.OldValue is DisplayImage old && e.NewValue is DisplayImage image
            && (old.ImageWidth != image.ImageWidth || old.ImageHeight != image.ImageHeight))
        {
            ((EditorCanvas)d)._zoomed = null;
        }
    }

    private static void OnResetViewRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EditorCanvas)d)._zoomed = null;

    // ---------------------------------------------------------------- ズーム（設計書 F-03）

    /// <summary>1 DIP あたりの物理ピクセル（ICanvasHost）</summary>
    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private Point ViewCenter => new(RenderSize.Width / 2, RenderSize.Height / 2);

    private ViewTransform FitView(DisplayImage image)
        => ViewTransform.Fit(RenderSize, image.ImageWidth, image.ImageHeight, DpiScale, FitMargin);

    /// <summary>今の倍率と位置（全体表示なら、今の大きさでの全体表示）</summary>
    private ViewTransform CurrentView(DisplayImage image) => _zoomed ?? FitView(image);

    /// <summary>メニュー［表示］とツールバーのズーム（中央が中心）</summary>
    public void Zoom(ZoomAction action)
    {
        if (Image is not { } image) return;
        switch (action)
        {
            case ZoomAction.In:
                ZoomAt(ViewCenter, ViewTransform.ZoomStep);
                break;
            case ZoomAction.Out:
                ZoomAt(ViewCenter, 1 / ViewTransform.ZoomStep);
                break;
            case ZoomAction.Fit:
                _zoomed = null;
                InvalidateVisual();
                break;
            case ZoomAction.ActualSize:
                ZoomAt(ViewCenter, 1 / DpiScale / CurrentView(image).Scale);
                break;
        }
    }

    /// <summary>viewPoint を中心に、倍率を factor 倍にする（範囲は「全体表示の倍率と 10% の小さい方」〜 3200%）</summary>
    public void ZoomAt(Point viewPoint, double factor)
    {
        if (Image is not { } image || RenderSize.Width <= 0 || RenderSize.Height <= 0) return;
        var (min, max) = ViewTransform.ScaleRange(FitView(image).Scale, DpiScale);
        _zoomed = CurrentView(image).ZoomAt(viewPoint, factor, min, max)
            .ClampPan(RenderSize, image.ImageWidth, image.ImageHeight);
        // パン中にズームしたら、パンの起点を今の位置と表示にし直す（次に動かしたときにズームを打ち消さないため）
        if (_pan is { } pan) _pan = (Mouse.GetPosition(this), _zoomed.Value, pan.Button);
        InvalidateVisual();
    }

    /// <summary>ホイール: カーソル位置を中心にズームする（1 ノッチで 1.25 倍。細かく回せるホイールは、その分だけ）</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Image is null || e.Delta == 0) return;
        ZoomAt(e.GetPosition(this), Math.Pow(ViewTransform.ZoomStep, e.Delta / (double)Mouse.MouseWheelDeltaForOneLine));
        e.Handled = true;
    }

    // ---------------------------------------------------------------- パン（設計書 F-03・4.5）

    /// <summary>パン中か</summary>
    public bool IsPanning => _pan is not null;

    /// <summary>
    /// 中ボタンのドラッグと、Space を押しながらの左ドラッグはパン（道具にかかわらず）。そのほかは今の道具に渡す
    /// </summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || Image is null || _pan is not null) return;
        Focus();

        bool middle = e.ChangedButton == MouseButton.Middle;
        bool spaceDrag = e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space);
        if (middle || spaceDrag)
        {
            BeginPan(MakePointer(e, e.ChangedButton));
            e.Handled = true;
            return;
        }

        if (_tool is { } tool)
        {
            tool.OnPointerDown(MakePointer(e, e.ChangedButton));
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pan is { } pan)
        {
            e.Handled = true;
            if (_zoomed is null || Image is not { } image) return; // パン中に全体表示に戻った（Ctrl+0 など）

            var delta = e.GetPosition(this) - pan.Start;
            _zoomed = pan.View.Pan(delta).ClampPan(RenderSize, image.ImageWidth, image.ImageHeight);
            InvalidateVisual();
            return;
        }

        _tool?.OnPointerMove(MakePointer(e, PressedButton(e)));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pan is { } pan)
        {
            if (e.ChangedButton == pan.Button) ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (_tool is { } tool)
        {
            tool.OnPointerUp(MakePointer(e, e.ChangedButton));
            e.Handled = true;
        }
    }

    /// <summary>
    /// pointer の位置からパンを始める（ICanvasHost）。pointer のボタンを離すまで、マウスに合わせて表示を動かす。
    /// 全体表示のときは、画像が領域に収まっているので何もしない（設計書 4.5）
    /// </summary>
    public void BeginPan(CanvasPointer pointer)
    {
        if (_pan is not null || _zoomed is not { } view || pointer.Button is not { } button) return;
        _pan = (pointer.ViewPoint, view, button);
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    /// <summary>パンできるか（ICanvasHost。ズーム中）</summary>
    public bool CanPan => _zoomed is not null && Image is not null;

    /// <summary>今の表示倍率と位置（ICanvasHost。描いているとおり、物理ピクセルにそろえたもの）</summary>
    public ViewTransform View => Image is { } image ? CurrentView(image).SnapToPixels(DpiScale) : default;

    private CanvasPointer MakePointer(MouseEventArgs e, MouseButton? button)
    {
        var viewPoint = e.GetPosition(this);
        var imagePoint = Image is null ? default : View.ViewToImage(viewPoint);
        return new CanvasPointer(viewPoint, imagePoint, button, Keyboard.Modifiers);
    }

    private static MouseButton? PressedButton(MouseEventArgs e)
        => e.LeftButton == MouseButtonState.Pressed ? MouseButton.Left
         : e.RightButton == MouseButtonState.Pressed ? MouseButton.Right
         : e.MiddleButton == MouseButtonState.Pressed ? MouseButton.Middle
         : null;

    /// <summary>
    /// 修飾キーの無い（または Shift だけの）キーを今の道具に渡す（メインウィンドウの PreviewKeyDown から。設計書 5.10-7）。処理したら true
    /// </summary>
    public bool HandleToolKey(Key key, ModifierKeys modifiers) => Image is not null && _tool?.OnKeyDown(key, modifiers) == true;

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndPan();
    }

    private void EndPan()
    {
        if (_pan is null) return;
        _pan = null;
        ClearValue(CursorProperty);
    }

    /// <summary>カーソル: パン中は移動。ズーム中に Space を押していれば移動。そのほかは今の道具が決める</summary>
    protected override void OnQueryCursor(QueryCursorEventArgs e)
    {
        base.OnQueryCursor(e);
        if (_pan is not null || Image is null) return;

        Cursor? cursor = _zoomed is not null && Keyboard.IsKeyDown(Key.Space)
            ? Cursors.SizeAll
            : _tool?.GetCursor(MakePointer(e, PressedButton(e)));
        if (cursor is not null)
        {
            e.Cursor = cursor;
            e.Handled = true;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        // ズーム中は、倍率と表示の中心を保つ（全体表示のときは、描くときに全体表示し直す）
        if (_zoomed is { } zoomed && Image is { } image && sizeInfo.PreviousSize.Width > 0 && sizeInfo.PreviousSize.Height > 0)
        {
            _zoomed = zoomed.Resized(sizeInfo.PreviousSize, sizeInfo.NewSize)
                .ClampPan(sizeInfo.NewSize, image.ImageWidth, image.ImageHeight);
        }
        InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // ズーム中は、表示倍率（%）を保つ（100% は、どのモニターでも 1 px = 1 物理ピクセル。設計書 5.10-8）
        if (_zoomed is { } zoomed && Image is { } image && RenderSize.Width > 0)
        {
            var (min, max) = ViewTransform.ScaleRange(FitView(image).Scale, newDpi.DpiScaleX);
            _zoomed = zoomed.ZoomAt(ViewCenter, oldDpi.DpiScaleX / newDpi.DpiScaleX, min, max)
                .ClampPan(RenderSize, image.ImageWidth, image.ImageHeight);
        }
        InvalidateVisual();
    }

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

        if (Image is not { } image)
        {
            UpdateZoomPercent(0);
            return;
        }
        double dpiScale = DpiScale;
        // 位置は物理ピクセルの境目にそろえて描く（ズーム中も、画素がにじまないように）
        var view = CurrentView(image).SnapToPixels(dpiScale);
        var rect = view.ImageBounds(image.ImageWidth, image.ImageHeight);
        UpdateZoomPercent(view.ZoomPercent(dpiScale));

        // 透過のある画像だけ、下に市松模様を描く（模様は画像の左上からそろえる。拡大中は、見えている所だけ）
        if (!image.IsOpaque)
        {
            var visible = Rect.Intersect(rect, new Rect(RenderSize));
            if (!visible.IsEmpty)
            {
                visible.Offset(-rect.X, -rect.Y);
                dc.PushTransform(new TranslateTransform(rect.X, rect.Y));
                dc.DrawRectangle(GetCheckerBrush(), null, visible);
                dc.Pop();
            }
        }

        var drawing = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(drawing, GetScalingMode(rect.Width, image.Bitmap.PixelWidth, dpiScale));
        using (var context = drawing.Open())
        {
            context.DrawImage(image.Bitmap, rect);
        }
        dc.DrawDrawing(drawing);

        // 選択範囲の縁と、道具の重ね描き（ハンドルなど）。
        // 画像の切り替えの途中で、窓口と画像の大きさが一時的に合わないときは描かない
        if (Session is { } session && session.ImageWidth == image.ImageWidth && session.ImageHeight == image.ImageHeight)
        {
            if (session.Selection is { } selection)
                RenderSelection(dc, ImageRectToView(selection, view, dpiScale), dpiScale);
            _tool?.Render(dc, view);
        }
    }

    private void UpdateZoomPercent(double percent)
    {
        if (ZoomPercent != percent) SetCurrentValue(ZoomPercentProperty, percent);
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
