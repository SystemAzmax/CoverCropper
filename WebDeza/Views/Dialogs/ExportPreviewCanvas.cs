using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WebDeza.ViewModels;
using WebDeza.Views.Canvas;

namespace WebDeza.Views.Dialogs;

/// <summary>
/// 保存のダイアログのプレビューの表示倍率と位置（設計書 F-09）。キャンバスから切り離して持つのは、
/// 「並べて比較」（10-9）で、圧縮前と圧縮後の 2 つのキャンバスが 1 つを共有して、ズーム・パンを連動させるため。
/// </summary>
public sealed class PreviewViewport
{
    /// <summary>
    /// 今の倍率と位置。null なら全体表示（描くたびに、今の大きさで全体表示する）。
    /// 最初は 100%（IsInitial）で、最初に描くときに画像の中央を出す
    /// </summary>
    public ViewTransform? Zoomed { get; private set; }

    /// <summary>まだ最初の 100% の位置を決めていないか</summary>
    public bool IsInitial { get; private set; } = true;

    /// <summary>倍率か位置が変わった（共有しているキャンバスが描き直す）</summary>
    public event EventHandler? Changed;

    /// <summary>倍率と位置を変える（null なら全体表示）</summary>
    public void Set(ViewTransform? view)
    {
        IsInitial = false;
        Zoomed = view;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>最初の状態（100% で中央）に戻す（画像の大きさが変わったとき）</summary>
    public void Reset()
    {
        IsInitial = true;
        Zoomed = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// 保存のダイアログのプレビュー（設計書 F-09）。画像を表示し、ズーム（ホイール・Zoom）とパン（左か中ボタンのドラッグ）ができる。
/// 道具は使わない。倍率の基準と範囲・補間・物理ピクセルへのそろえは、子ウィンドウのキャンバス（EditorCanvas）と同じ。
/// 既定の倍率は 100%（画像の 1 px = 1 物理ピクセル）で、画像の中央を出す。
/// </summary>
public sealed class ExportPreviewCanvas : FrameworkElement
{
    /// <summary>全体表示のときに、画像の周りに空ける余白（DIP）</summary>
    public const double FitMargin = 12;

    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image), typeof(BitmapSource), typeof(ExportPreviewCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnImageChanged));

    public static readonly DependencyProperty ViewportProperty = DependencyProperty.Register(
        nameof(Viewport), typeof(PreviewViewport), typeof(ExportPreviewCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewportChanged));

    private static readonly DependencyPropertyKey ZoomTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ZoomText), typeof(string), typeof(ExportPreviewCanvas), new PropertyMetadata(""));

    public static readonly DependencyProperty ZoomTextProperty = ZoomTextPropertyKey.DependencyProperty;

    public static readonly DependencyProperty CheckerLightColorProperty = DependencyProperty.Register(
        nameof(CheckerLightColor), typeof(Color), typeof(ExportPreviewCanvas),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender, OnCheckerColorChanged));

    public static readonly DependencyProperty CheckerDarkColorProperty = DependencyProperty.Register(
        nameof(CheckerDarkColor), typeof(Color), typeof(ExportPreviewCanvas),
        new FrameworkPropertyMetadata(Color.FromRgb(0xCC, 0xCC, 0xCC), FrameworkPropertyMetadataOptions.AffectsRender, OnCheckerColorChanged));

    private Brush? _checkerBrush;

    /// <summary>パン中の、始めた点と、そのときの表示</summary>
    private (Point Start, ViewTransform View, MouseButton Button)? _pan;

    public ExportPreviewCanvas()
    {
        Viewport = new PreviewViewport();
        Focusable = false;
        ClipToBounds = true;
    }

    /// <summary>表示する画像</summary>
    public BitmapSource? Image
    {
        get => (BitmapSource?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    /// <summary>表示倍率と位置（共有すると、ズーム・パンが連動する）</summary>
    public PreviewViewport Viewport
    {
        get => (PreviewViewport)GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    /// <summary>表示倍率の文字（例: 「100%」。画像が無ければ空）</summary>
    public string ZoomText => (string)GetValue(ZoomTextProperty);

    public Color CheckerLightColor
    {
        get => (Color)GetValue(CheckerLightColorProperty);
        set => SetValue(CheckerLightColorProperty, value);
    }

    public Color CheckerDarkColor
    {
        get => (Color)GetValue(CheckerDarkColorProperty);
        set => SetValue(CheckerDarkColorProperty, value);
    }

    /// <summary>1 DIP あたりの物理ピクセル</summary>
    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private static void OnImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 大きさが変わったら（初めての画像も）、100% で中央に戻す。同じ大きさなら（設定を変えて作り直した）、倍率と位置を保つ
        if (e.OldValue is not BitmapSource old || e.NewValue is not BitmapSource image
            || old.PixelWidth != image.PixelWidth || old.PixelHeight != image.PixelHeight)
        {
            ((ExportPreviewCanvas)d).Viewport?.Reset();
        }
    }

    private static void OnViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (ExportPreviewCanvas)d;
        if (e.OldValue is PreviewViewport old) old.Changed -= canvas.OnViewportStateChanged;
        if (e.NewValue is PreviewViewport viewport) viewport.Changed += canvas.OnViewportStateChanged;
    }

    private void OnViewportStateChanged(object? sender, EventArgs e) => InvalidateVisual();

    private static void OnCheckerColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ExportPreviewCanvas)d)._checkerBrush = null;

    // ---------------------------------------------------------------- 倍率と位置

    private int ImageWidth => Image?.PixelWidth ?? 0;
    private int ImageHeight => Image?.PixelHeight ?? 0;

    private Point ViewCenter => new(RenderSize.Width / 2, RenderSize.Height / 2);

    private ViewTransform FitView() => ViewTransform.Fit(RenderSize, ImageWidth, ImageHeight, DpiScale, FitMargin);

    /// <summary>100%（画像の 1 px = 1 物理ピクセル）で、画像の中央を出す表示</summary>
    private ViewTransform ActualSizeView()
    {
        double scale = 1 / DpiScale;
        var view = new ViewTransform(scale,
            (RenderSize.Width - ImageWidth * scale) / 2,
            (RenderSize.Height - ImageHeight * scale) / 2);
        return view.ClampPan(RenderSize, ImageWidth, ImageHeight);
    }

    /// <summary>今の倍率と位置（最初は 100% で中央、全体表示なら今の大きさでの全体表示）</summary>
    private ViewTransform CurrentView()
    {
        var viewport = Viewport;
        if (viewport.IsInitial)
        {
            if (RenderSize.Width > 0 && RenderSize.Height > 0 && Image is not null) viewport.Set(ActualSizeView());
            else return FitView();
        }
        return viewport.Zoomed ?? FitView();
    }

    /// <summary>ダイアログのボタンのズーム（中央が中心）</summary>
    public void Zoom(ZoomAction action)
    {
        if (Image is null) return;
        switch (action)
        {
            case ZoomAction.In:
                ZoomAt(ViewCenter, ViewTransform.ZoomStep);
                break;
            case ZoomAction.Out:
                ZoomAt(ViewCenter, 1 / ViewTransform.ZoomStep);
                break;
            case ZoomAction.Fit:
                Viewport.Set(null);
                break;
            case ZoomAction.ActualSize:
                ZoomAt(ViewCenter, 1 / DpiScale / CurrentView().Scale);
                break;
        }
    }

    /// <summary>viewPoint を中心に、倍率を factor 倍にする（範囲は子ウィンドウのキャンバスと同じ）</summary>
    public void ZoomAt(Point viewPoint, double factor)
    {
        if (Image is null || RenderSize.Width <= 0 || RenderSize.Height <= 0) return;
        var (min, max) = ViewTransform.ScaleRange(FitView().Scale, DpiScale);
        var view = CurrentView().ZoomAt(viewPoint, factor, min, max).ClampPan(RenderSize, ImageWidth, ImageHeight);
        if (_pan is { } pan) _pan = (Mouse.GetPosition(this), view, pan.Button);
        Viewport.Set(view);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Image is null || e.Delta == 0) return;
        ZoomAt(e.GetPosition(this), Math.Pow(ViewTransform.ZoomStep, e.Delta / (double)Mouse.MouseWheelDeltaForOneLine));
        e.Handled = true;
    }

    // ---------------------------------------------------------------- パン（設計書 4.5「ドラッグ（保存のプレビュー）」）

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || Image is null || _pan is not null) return;
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle)) return;

        // 全体表示のときは、画像が領域に収まっているので何もしない
        if (Viewport.Zoomed is not { } view) return;
        _pan = (e.GetPosition(this), view, e.ChangedButton);
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pan is not { } pan) return;
        e.Handled = true;
        Viewport.Set(pan.View.Pan(e.GetPosition(this) - pan.Start).ClampPan(RenderSize, ImageWidth, ImageHeight));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pan is { } pan && e.ChangedButton == pan.Button)
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _pan = null;
        ClearValue(CursorProperty);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // ズーム中は、倍率と表示の中心を保つ（全体表示のときは、描くときに全体表示し直す）
        if (Viewport is { IsInitial: false, Zoomed: { } zoomed } viewport && Image is not null
            && sizeInfo.PreviousSize.Width > 0 && sizeInfo.PreviousSize.Height > 0)
        {
            viewport.Set(zoomed.Resized(sizeInfo.PreviousSize, sizeInfo.NewSize)
                .ClampPan(sizeInfo.NewSize, ImageWidth, ImageHeight));
        }
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- 描画

    protected override void OnRender(DrawingContext dc)
    {
        // マウス操作を受けるため、全面にヒットテストできる背景を敷く（背景の色は親が持つ）
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Image is not { } image || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            SetValue(ZoomTextPropertyKey, "");
            return;
        }

        double dpiScale = DpiScale;
        var view = CurrentView().SnapToPixels(dpiScale);
        var rect = view.ImageBounds(ImageWidth, ImageHeight);
        SetValue(ZoomTextPropertyKey, MainViewModel.FormatZoomPercent(view.ZoomPercent(dpiScale)));

        // 透過のある画像が分かるよう、下に市松模様を描く（不透明な画像では隠れる。模様は画像の左上からそろえる）
        var visible = Rect.Intersect(rect, new Rect(RenderSize));
        if (!visible.IsEmpty)
        {
            visible.Offset(-rect.X, -rect.Y);
            dc.PushTransform(new TranslateTransform(rect.X, rect.Y));
            dc.DrawRectangle(GetCheckerBrush(), null, visible);
            dc.Pop();
        }

        var drawing = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(drawing, EditorCanvas.GetScalingMode(rect.Width, image.PixelWidth, dpiScale));
        using (var context = drawing.Open())
        {
            context.DrawImage(image, rect);
        }
        dc.DrawDrawing(drawing);
    }

    private Brush GetCheckerBrush()
    {
        if (_checkerBrush is not null) return _checkerBrush;
        double size = EditorCanvas.CheckerSize;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(CheckerLightColor), null, new RectangleGeometry(new Rect(0, 0, size * 2, size * 2))));
        var dark = new SolidColorBrush(CheckerDarkColor);
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(size, 0, size, size))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, size, size, size))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size * 2, size * 2),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        _checkerBrush = brush;
        return brush;
    }
}
