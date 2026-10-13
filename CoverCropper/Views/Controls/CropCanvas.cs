using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoverCropper.Services;

namespace CoverCropper.Views.Controls;

/// <summary>
/// リサイズ後画像を Fit 表示し、1024×768 の切り抜き枠を描画・ドラッグ移動するコントロール。
/// 枠位置（CropX/CropY）は画像ピクセル座標の整数で保持し、描画時にのみ表示倍率を掛ける。
/// </summary>
public sealed class CropCanvas : FrameworkElement
{
    private const double ViewMargin = 12;

    private static readonly IImageCropper Cropper = new ImageCropper();

    private static readonly Brush OverlayBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)));
    private static readonly Pen FrameOuterPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), 3));
    private static readonly Pen FrameInnerPen = Freeze(new Pen(Brushes.White, 1.5));
    private static readonly Pen GuidePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1)
    {
        DashStyle = DashStyles.Dash,
    });

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(CropCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CropXProperty = DependencyProperty.Register(
        nameof(CropX), typeof(int), typeof(CropCanvas),
        new FrameworkPropertyMetadata(0,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CropYProperty = DependencyProperty.Register(
        nameof(CropY), typeof(int), typeof(CropCanvas),
        new FrameworkPropertyMetadata(0,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private bool _dragging;
    private Point _dragStart;
    private int _dragOriginX, _dragOriginY;

    public CropCanvas()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        ClipToBounds = true;
    }

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public int CropX
    {
        get => (int)GetValue(CropXProperty);
        set => SetValue(CropXProperty, value);
    }

    public int CropY
    {
        get => (int)GetValue(CropYProperty);
        set => SetValue(CropYProperty, value);
    }

    /// <summary>表示倍率と画像の表示矩形を求める（拡大表示はしない）</summary>
    private bool TryGetLayout(out double scale, out Rect imageRect)
    {
        scale = 0;
        imageRect = Rect.Empty;
        var source = Source;
        if (source is null) return false;

        double availW = Math.Max(1, ActualWidth - ViewMargin * 2);
        double availH = Math.Max(1, ActualHeight - ViewMargin * 2);
        scale = Math.Min(1.0, Math.Min(availW / source.PixelWidth, availH / source.PixelHeight));

        double w = source.PixelWidth * scale;
        double h = source.PixelHeight * scale;
        imageRect = new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);
        return true;
    }

    private Rect GetCropRect(double scale, Rect imageRect) => new(
        imageRect.X + CropX * scale,
        imageRect.Y + CropY * scale,
        AppConstants.TargetWidth * scale,
        AppConstants.TargetHeight * scale);

    protected override void OnRender(DrawingContext dc)
    {
        // マウス操作を受けるため全面にヒットテスト可能な背景を敷く
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (!TryGetLayout(out double scale, out Rect imageRect)) return;

        dc.DrawImage(Source, imageRect);

        var crop = GetCropRect(scale, imageRect);

        // 枠外を暗くする（画像矩形 − 切り抜き矩形）
        var overlay = new GeometryGroup { FillRule = FillRule.EvenOdd };
        overlay.Children.Add(new RectangleGeometry(imageRect));
        overlay.Children.Add(new RectangleGeometry(crop));
        dc.DrawGeometry(OverlayBrush, null, overlay);

        // 三分割ガイド
        for (int i = 1; i <= 2; i++)
        {
            double gx = crop.X + crop.Width * i / 3;
            double gy = crop.Y + crop.Height * i / 3;
            dc.DrawLine(GuidePen, new Point(gx, crop.Top), new Point(gx, crop.Bottom));
            dc.DrawLine(GuidePen, new Point(crop.Left, gy), new Point(crop.Right, gy));
        }

        dc.DrawRectangle(null, FrameOuterPen, crop);
        dc.DrawRectangle(null, FrameInnerPen, crop);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!TryGetLayout(out double scale, out Rect imageRect)) return;

        var p = e.GetPosition(this);
        if (!GetCropRect(scale, imageRect).Contains(p)) return;

        _dragging = true;
        _dragStart = p;
        _dragOriginX = CropX;
        _dragOriginY = CropY;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!TryGetLayout(out double scale, out Rect imageRect))
        {
            Cursor = null;
            return;
        }

        var p = e.GetPosition(this);
        if (_dragging)
        {
            // 表示座標の移動量を画像ピクセルに換算
            int x = _dragOriginX + (int)Math.Round((p.X - _dragStart.X) / scale);
            int y = _dragOriginY + (int)Math.Round((p.Y - _dragStart.Y) / scale);
            var source = Source!;
            (x, y) = Cropper.Clamp(x, y, source.PixelWidth, source.PixelHeight);

            if (x != CropX) SetCurrentValue(CropXProperty, x);
            if (y != CropY) SetCurrentValue(CropYProperty, y);
        }
        else
        {
            Cursor = GetCropRect(scale, imageRect).Contains(p) ? Cursors.SizeAll : null;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
