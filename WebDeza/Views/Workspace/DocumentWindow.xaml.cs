using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WebDeza.ViewModels;
using WebDeza.Views.Canvas;

namespace WebDeza.Views.Workspace;

/// <summary>
/// 子ウィンドウ 1 つ（設計書 F-02・5.6）。タイトルバー（ファイル名・最大化 / 元に戻す・閉じる）とキャンバス。
/// タイトルバーのドラッグで移動、縁と角のドラッグで大きさを変える。位置と大きさは DocumentViewModel.WindowBounds に書き、
/// 置くのは DocumentWorkspace が行う。
/// </summary>
/// <remarks>
/// どこをクリックしても、その画像がアクティブになる。タイトルバーには、書き出していない編集の印「*」も出す（DocumentViewModel.Title）。
/// </remarks>
public partial class DocumentWindow : UserControl
{
    /// <summary>タイトルバーの高さ（DIP）</summary>
    public const double TitleBarHeight = 30;

    /// <summary>枠の太さ（DIP）</summary>
    public const double FrameThickness = 1;

    /// <summary>大きさを変えるためにつかめる縁の幅（DIP）</summary>
    public const double GripThickness = 5;

    /// <summary>大きさを変えるためにつかめる角の大きさ（DIP）</summary>
    public const double CornerGripSize = 12;

    /// <summary>
    /// 子ウィンドウのうち、画像を描かない部分の幅と高さ（枠・タイトルバー・キャンバスの余白）。
    /// 新しく開いたときに、画像が全体表示で収まる大きさを求めるのに使う
    /// </summary>
    public static Size Chrome { get; } = new(
        FrameThickness * 2 + EditorCanvas.FitMargin * 2,
        FrameThickness * 2 + TitleBarHeight + EditorCanvas.FitMargin * 2);

    // Segoe Fluent Icons の「最大化」「元に戻す」
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";

    public static readonly DependencyProperty IsMaximizedProperty = DependencyProperty.Register(
        nameof(IsMaximized), typeof(bool), typeof(DocumentWindow),
        new FrameworkPropertyMetadata(false, (d, _) => ((DocumentWindow)d).UpdateMaximizedState()));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(DocumentWindow),
        new FrameworkPropertyMetadata(false, (d, _) => ((DocumentWindow)d).UpdateActiveState()));

    private enum DragKind { None, Move, Resize }

    private DragKind _drag;
    private ResizeEdges _dragEdges;
    private Point _dragStartPoint;
    private Rect _dragStartBounds;

    public DocumentWindow()
    {
        InitializeComponent();
        Frame.BorderThickness = new Thickness(FrameThickness);
        TitleBar.Height = TitleBarHeight;
        UpdateMaximizedState();
        UpdateActiveState();
    }

    /// <summary>この子ウィンドウを置いているワークスペース（移動と大きさの計算に、その大きさを使う）</summary>
    internal DocumentWorkspace? Workspace { get; set; }

    /// <summary>最大化しているか（ワークスペース全体で 1 つの状態を、ワークスペースが入れる）</summary>
    public bool IsMaximized
    {
        get => (bool)GetValue(IsMaximizedProperty);
        set => SetValue(IsMaximizedProperty, value);
    }

    /// <summary>アクティブな画像の子ウィンドウか（ワークスペースが入れる）。タイトルバーの見た目が変わる</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private DocumentViewModel? Document => DataContext as DocumentViewModel;

    /// <summary>この子ウィンドウのキャンバス（メニューとツールバーのズームを伝える先）</summary>
    public EditorCanvas Canvas => ImageCanvas;

    private void UpdateActiveState()
    {
        TitleBar.Style = (Style)FindResource(IsActive ? "DocumentTitleBarActiveStyle" : "DocumentTitleBarStyle");
        TitleText.Style = (Style)FindResource(IsActive ? "DocumentTitleTextActiveStyle" : "DocumentTitleTextStyle");
    }

    /// <summary>子ウィンドウのどこかが押されたら、その画像をアクティブにする（押したものの処理はそのまま続ける）</summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Document is { } document) Workspace?.Activate(document);
    }

    private void UpdateMaximizedState()
    {
        MaximizeButton.Content = IsMaximized ? RestoreGlyph : MaximizeGlyph;
        MaximizeButton.ToolTip = IsMaximized ? "元に戻す" : "最大化";
        ResizeGrips.Visibility = IsMaximized ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnMaximizeButtonClick(object sender, RoutedEventArgs e) => ToggleMaximized();

    private void ToggleMaximized()
    {
        if (Workspace is not null) Workspace.IsMaximized = !Workspace.IsMaximized;
    }

    // ---------------------------------------------------------------- 移動と大きさの変更

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximized();
            e.Handled = true;
            return;
        }
        if (!IsMaximized) StartDrag((UIElement)sender, DragKind.Move, ResizeEdges.None, e);
    }

    private void OnResizeGripMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsMaximized && sender is FrameworkElement { Tag: ResizeEdges edges })
            StartDrag((UIElement)sender, DragKind.Resize, edges, e);
    }

    private void StartDrag(UIElement element, DragKind kind, ResizeEdges edges, MouseButtonEventArgs e)
    {
        if (Workspace is null || Document is not { WindowBounds: { } bounds }) return;

        // 位置はワークスペースの座標で測る（子ウィンドウ自身が動くため）。始めたときからの移動量で計算し、ずれをためない
        _drag = kind;
        _dragEdges = edges;
        _dragStartPoint = e.GetPosition(Workspace);
        _dragStartBounds = bounds;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMouseMove(object sender, MouseEventArgs e)
    {
        if (_drag == DragKind.None || Workspace is null || Document is not { } document) return;

        var delta = e.GetPosition(Workspace) - _dragStartPoint;
        var workspace = Workspace.RenderSize;
        document.WindowBounds = _drag == DragKind.Move
            ? WindowLayout.Move(_dragStartBounds, delta, workspace, TitleBarHeight)
            : WindowLayout.Resize(_dragStartBounds, _dragEdges, delta, workspace, TitleBarHeight);
        e.Handled = true;
    }

    private void OnDragMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == DragKind.None) return;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnDragLostMouseCapture(object sender, MouseEventArgs e) => _drag = DragKind.None;
}
