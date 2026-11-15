using System.Windows;
using System.Windows.Media;
using WebDeza.ViewModels;

namespace WebDeza.Views.Canvas;

/// <summary>
/// 中央のキャンバス（設計書 F-03・5.6）。市松模様の上に表示用画像を描く。
/// </summary>
/// <remarks>
/// 表示用画像は、編集画像（フル解像度）の大きさとして描く。切り抜きの枠（5-6）は編集画像の座標で重ねる。
/// 今は全体表示だけ。倍率と位置の計算は 6-1 で ViewTransform に移し、ズーム（6-2）とパン（6-3）を足す。
/// </remarks>
public sealed class EditorCanvas : FrameworkElement
{
    /// <summary>全体表示のときに、画像の周りに空ける余白（DIP。CoverCropper と同じ）</summary>
    public const double FitMargin = 12;

    /// <summary>市松模様の 1 マスの大きさ（DIP。設計書 F-03）</summary>
    public const double CheckerSize = 8;

    /// <summary>表示用画像の 1 px がこの物理ピクセル数以上になったら、補間をやめて画素をそのまま拡大する（設計書 F-03）</summary>
    public const double NearestNeighborThreshold = 2;

    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image), typeof(DisplayImage), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ResetViewRequestProperty = DependencyProperty.Register(
        nameof(ResetViewRequest), typeof(int), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CheckerLightColorProperty = DependencyProperty.Register(
        nameof(CheckerLightColor), typeof(Color), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender, OnCheckerColorChanged));

    public static readonly DependencyProperty CheckerDarkColorProperty = DependencyProperty.Register(
        nameof(CheckerDarkColor), typeof(Color), typeof(EditorCanvas),
        new FrameworkPropertyMetadata(Color.FromRgb(0xCC, 0xCC, 0xCC), FrameworkPropertyMetadataOptions.AffectsRender, OnCheckerColorChanged));

    private Brush? _checkerBrush;

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

    private static void OnCheckerColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EditorCanvas)d)._checkerBrush = null;

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

    protected override void OnRender(DrawingContext dc)
    {
        // マウス操作を受けるため、全面にヒットテストできる背景を敷く（背景の色は親の Grid が持つ）
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (Image is not { } image) return;

        double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var (rect, _) = Fit(RenderSize, image.EditedWidth, image.EditedHeight, dpiScale);

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
