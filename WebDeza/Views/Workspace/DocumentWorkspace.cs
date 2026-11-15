using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WebDeza.ViewModels;

namespace WebDeza.Views.Workspace;

/// <summary>
/// 子ウィンドウを並べる領域（設計書 F-02・5.6）。Documents の 1 件ごとに DocumentWindow を 1 つ作り、
/// DocumentViewModel.WindowBounds の位置と大きさに置く。手前・奥の順番、アクティブな画像の切り替え、
/// 最大化（ワークスペース全体で 1 つの状態）、ワークスペースの大きさが変わったときの寄せ直しを受け持つ。
/// </summary>
/// <remarks>
/// WPF には MDI の仕組みが無いので自前で作る。重ねて表示・並べて表示は 7-4 で足す。
/// </remarks>
public sealed class DocumentWorkspace : Panel
{
    public static readonly DependencyProperty DocumentsProperty = DependencyProperty.Register(
        nameof(Documents), typeof(IEnumerable), typeof(DocumentWorkspace),
        new FrameworkPropertyMetadata(null, OnDocumentsChanged));

    public static readonly DependencyProperty ActiveDocumentProperty = DependencyProperty.Register(
        nameof(ActiveDocument), typeof(DocumentViewModel), typeof(DocumentWorkspace),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnActiveDocumentChanged));

    public static readonly DependencyProperty IsMaximizedProperty = DependencyProperty.Register(
        nameof(IsMaximized), typeof(bool), typeof(DocumentWorkspace),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsMaximizedChanged));

    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
        nameof(CloseCommand), typeof(ICommand), typeof(DocumentWorkspace));

    private readonly Dictionary<DocumentViewModel, DocumentWindow> _windows = new();

    /// <summary>手前・奥の順番（最後がいちばん手前）</summary>
    private readonly List<DocumentViewModel> _zOrder = new();

    public DocumentWorkspace()
    {
        ClipToBounds = true;
    }

    /// <summary>開いている画像（DocumentViewModel の並び。INotifyCollectionChanged なら変化に追従する）</summary>
    public IEnumerable? Documents
    {
        get => (IEnumerable?)GetValue(DocumentsProperty);
        set => SetValue(DocumentsProperty, value);
    }

    /// <summary>アクティブな画像。その子ウィンドウをいちばん手前に出す。子ウィンドウをクリックすると、その画像になる</summary>
    public DocumentViewModel? ActiveDocument
    {
        get => (DocumentViewModel?)GetValue(ActiveDocumentProperty);
        set => SetValue(ActiveDocumentProperty, value);
    }

    /// <summary>
    /// 子ウィンドウを最大化しているか。最大化している間は、すべての子ウィンドウをワークスペースいっぱいに置く
    /// （どの画像に切り替えても最大化で表示する。設計書 F-02）
    /// </summary>
    public bool IsMaximized
    {
        get => (bool)GetValue(IsMaximizedProperty);
        set => SetValue(IsMaximizedProperty, value);
    }

    /// <summary>子ウィンドウの［閉じる］で実行するコマンド（引数は DocumentViewModel）</summary>
    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>画像の子ウィンドウ（無ければ null）</summary>
    public DocumentWindow? GetWindow(DocumentViewModel document)
        => _windows.TryGetValue(document, out var window) ? window : null;

    /// <summary>手前・奥の順番（奥から手前へ）</summary>
    public IReadOnlyList<DocumentViewModel> ZOrder => _zOrder;

    /// <summary>子ウィンドウがクリックされた: その画像をアクティブにする</summary>
    internal void Activate(DocumentViewModel document)
    {
        if (_windows.ContainsKey(document)) ActiveDocument = document;
    }

    // ---------------------------------------------------------------- 画像の一覧に合わせる

    private static void OnDocumentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var workspace = (DocumentWorkspace)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection) oldCollection.CollectionChanged -= workspace.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection) newCollection.CollectionChanged += workspace.OnCollectionChanged;
        workspace.SyncWindows();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncWindows();

    private List<DocumentViewModel> DocumentList => Documents?.OfType<DocumentViewModel>().ToList() ?? [];

    /// <summary>子ウィンドウを、画像の一覧に合わせる（無くなった画像の子ウィンドウを外し、増えた画像の子ウィンドウを作る）</summary>
    private void SyncWindows()
    {
        var documents = DocumentList;

        foreach (var document in _windows.Keys.Except(documents).ToList())
            RemoveWindow(document);

        foreach (var document in documents.Where(d => !_windows.ContainsKey(d)))
            AddWindow(document);

        UpdateZOrder();
        InvalidateMeasure();
    }

    private void AddWindow(DocumentViewModel document)
    {
        var window = new DocumentWindow
        {
            DataContext = document,
            Workspace = this,
            IsMaximized = IsMaximized,
            IsActive = document == ActiveDocument,
        };
        _windows.Add(document, window);
        _zOrder.Add(document); // 新しい子ウィンドウは手前に出す
        Children.Add(window);
        document.PropertyChanged += OnDocumentPropertyChanged;
        PlaceNewWindow(document);
    }

    /// <summary>子ウィンドウを外し、画像への参照をすべて外す（設計書 5.10-11）</summary>
    private void RemoveWindow(DocumentViewModel document)
    {
        var window = _windows[document];
        _windows.Remove(document);
        _zOrder.Remove(document);
        document.PropertyChanged -= OnDocumentPropertyChanged;
        Children.Remove(window);
        window.Workspace = null;
        window.DataContext = null;
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.WindowBounds)) InvalidateMeasure();
    }

    /// <summary>
    /// まだ置いていない子ウィンドウを置く。大きさは画像が全体表示で収まる大きさ（ワークスペースの 80% まで）、
    /// 位置は前に開いた子ウィンドウから右下へずらした所（設計書 F-02）
    /// </summary>
    private void PlaceNewWindow(DocumentViewModel document)
    {
        if (document.WindowBounds is not null) return;
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return; // まだ大きさが決まっていない（決まったら置く）

        // 前に開いた子ウィンドウ = 一覧でこの画像より前にあり、もう置いてあるもののうち最後
        var documents = DocumentList;
        int index = documents.IndexOf(document);
        var previous = documents.Take(Math.Max(0, index)).LastOrDefault(d => d.WindowBounds is not null);

        double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var size = WindowLayout.NewWindowSize(RenderSize, document.ImageWidth, document.ImageHeight, dpiScale, DocumentWindow.Chrome);
        var position = WindowLayout.NewWindowPosition(previous?.WindowBounds, size, RenderSize);
        document.WindowBounds = new Rect(position, size);
    }

    // ---------------------------------------------------------------- アクティブと手前・奥の順番

    private static void OnActiveDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var workspace = (DocumentWorkspace)d;
        if (e.NewValue is DocumentViewModel active && workspace._zOrder.Remove(active))
            workspace._zOrder.Add(active); // アクティブな子ウィンドウを、いちばん手前に出す
        workspace.UpdateZOrder();
    }

    /// <summary>子ウィンドウの重なりの順番と、アクティブの印を、今の状態に合わせる</summary>
    private void UpdateZOrder()
    {
        for (int i = 0; i < _zOrder.Count; i++)
        {
            var window = _windows[_zOrder[i]];
            SetZIndex(window, i);
            window.IsActive = _zOrder[i] == ActiveDocument;
        }
    }

    // ---------------------------------------------------------------- 最大化・大きさの変化

    private static void OnIsMaximizedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var workspace = (DocumentWorkspace)d;
        foreach (var window in workspace._windows.Values) window.IsMaximized = workspace.IsMaximized;
        workspace.KeepWindowsInside();
        workspace.InvalidateMeasure();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        foreach (var document in DocumentList.Where(_windows.ContainsKey)) PlaceNewWindow(document);
        KeepWindowsInside();
    }

    /// <summary>
    /// タイトルバーが外に出た子ウィンドウを、内側へ寄せる（設計書 F-02）。最大化している子ウィンドウは、
    /// 置くときにワークスペースの大きさに合わせるので、ここでは元に戻したときの位置と大きさを寄せておく
    /// </summary>
    private void KeepWindowsInside()
    {
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;
        foreach (var document in _windows.Keys)
        {
            if (document.WindowBounds is { } bounds)
                document.WindowBounds = WindowLayout.KeepInside(bounds, RenderSize, DocumentWindow.TitleBarHeight);
        }
    }

    // ---------------------------------------------------------------- 配置

    private Rect GetWindowRect(DocumentWindow window, Size workspace)
    {
        if (window.DataContext is not DocumentViewModel { WindowBounds: { } bounds }) return Rect.Empty;
        return IsMaximized ? new Rect(workspace) : bounds;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        foreach (DocumentWindow window in InternalChildren)
        {
            var rect = GetWindowRect(window, size);
            window.Measure(rect.IsEmpty ? default : rect.Size);
        }
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (DocumentWindow window in InternalChildren)
        {
            var rect = GetWindowRect(window, finalSize);
            window.Arrange(rect.IsEmpty ? default : rect);
        }
        return finalSize;
    }
}
