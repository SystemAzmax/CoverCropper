using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Services;
using WebDeza.Views.Canvas;

namespace WebDeza.ViewModels;

/// <summary>
/// 開いている画像 1 枚（設計書 5.6）。今の画像（フル解像度）・選択範囲・履歴・表示用画像を持ち、
/// キャンバスと道具の窓口（IEditorSession）になる。
/// </summary>
public sealed partial class DocumentViewModel : ObservableObject, IEditorSession
{
    private readonly Func<int, int, AspectRatio?> _selectionAspect;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;

    /// <summary>開いたとき（または最後に書き出したとき）の履歴の状態の番号</summary>
    private long _exportedStateId;

    /// <param name="path">元画像のフルパス</param>
    /// <param name="image">開いた画像（向きを直した Bgra32）</param>
    /// <param name="display">その表示用画像（バックグラウンドで作っておいたもの）</param>
    /// <param name="sourceFileSize">元ファイルのバイト数</param>
    /// <param name="selectionAspect">画像の幅・高さから、選択範囲の比率を返す（全画像共通の設定。null なら自由）</param>
    /// <param name="dialogs">編集できなかったときに M28 を出す</param>
    /// <param name="log">編集できなかったときに書くログ</param>
    public DocumentViewModel(string path, ImageBuffer image, DisplayImage display, long sourceFileSize,
        Func<int, int, AspectRatio?> selectionAspect, IDialogService dialogs, AppLog log)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(selectionAspect);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);
        if (display.ImageWidth != image.Width || display.ImageHeight != image.Height)
            throw new ArgumentException("表示用画像が、画像と同じ大きさのものではありません。", nameof(display));

        Path = path;
        SourceFileSize = sourceFileSize;
        History = new ImageHistory(image);
        _display = display;
        _selectionAspect = selectionAspect;
        _dialogs = dialogs;
        _log = log;
        _exportedStateId = History.StateId;
    }

    /// <summary>元画像のフルパス</summary>
    public string Path { get; }

    /// <summary>ファイル名（拡張子つき）</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>元ファイルのバイト数（書き出しサイズとの比率の表示に使う）</summary>
    public long SourceFileSize { get; }

    /// <summary>この画像の履歴（元に戻す / やり直しは画像ごと）</summary>
    public ImageHistory History { get; }

    /// <summary>今の画像（フル解像度。History.Current と同じ）</summary>
    public ImageBuffer Image => History.Current;

    /// <summary>キャンバスに描く表示用画像</summary>
    [ObservableProperty]
    private DisplayImage _display;

    /// <summary>
    /// キャンバスを全体表示に戻す合図。画像の大きさが変わる編集のたびに 1 増やす。
    /// ズームの状態は表示だけの関心事なので ViewModel には持たせない（設計書 5.6）
    /// </summary>
    [ObservableProperty]
    private int _resetViewRequest;

    /// <summary>
    /// 子ウィンドウの位置と大きさ（ワークスペースの左上を原点とする DIP）。null なら、まだ置いていない（ワークスペースが決める）。
    /// 最大化している間も、元に戻したときの位置と大きさとして持っておく（最大化はワークスペース全体で 1 つの状態。設計書 5.6）
    /// </summary>
    [ObservableProperty]
    private Rect? _windowBounds;

    /// <summary>
    /// 子ウィンドウのキャンバスの表示倍率（%）。キャンバスが書く、ステータスバーに出すための値で、0 なら不明。
    /// ズームの状態そのものはキャンバスが持つ（設計書 5.6）
    /// </summary>
    [ObservableProperty]
    private double _zoomPercent;

    /// <summary>アクティブな画像か（MainViewModel が ActiveDocument に合わせて入れる。メニュー［ウィンドウ］の一覧の印に使う）</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>書き出していない編集があるか（履歴の状態が、開いたとき・最後に書き出したときと違う）</summary>
    public bool HasUnexportedEdits => History.StateId != _exportedStateId;

    /// <summary>子ウィンドウのタイトルバーの文字: ファイル名。書き出していない編集があれば「 *」を続ける（設計書 4.1）</summary>
    public string Title => HasUnexportedEdits ? $"{FileName} *" : FileName;

    // ---------------------------------------------------------------- 編集の確定（設計書 5.6・5.8）

    private bool _isBusy;

    /// <summary>
    /// 編集の確定中か（このあいだは、ほかの編集を受け付けない）。MainViewModel はこれを見て、
    /// アプリ全体を確定中（メニューなどを無効にし、待機カーソル）にする
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// 画像を変える編集を確定する（画像を変える編集は、すべてここを通す）。edit はバックグラウンドで呼ぶ。
    /// 終わったら History に積み、表示用画像を作り直し、選択範囲を newSelection にする。
    /// 失敗したとき（例外・メモリ不足）は、画像も履歴も選択範囲も変えず、M28 を出してログに書く（M28 は確定中を解いてから出す）。
    /// 確定中に呼ばれたときは何もしない
    /// </summary>
    /// <param name="name">操作の名前（元に戻す・やり直しのメニューに出す）</param>
    /// <param name="edit">今の画像から、編集した画像を作る（入力は変えない。バックグラウンドで呼ぶ）</param>
    /// <param name="changedRegion">大きさが変わらず一部だけが変わるときの範囲（履歴がその範囲だけを持つ）。それ以外は null</param>
    /// <param name="newSelection">編集の後の選択範囲（切り抜き・回転なら null、フィルターなら今のまま）。編集の後の画像の内側に収める</param>
    /// <param name="resetView">全体表示に戻すか（画像の大きさが変わる編集）</param>
    public async Task ApplyEditAsync(string name, Func<ImageBuffer, CancellationToken, ImageBuffer> edit,
        PixelRect? changedRegion, PixelRect? newSelection, bool resetView)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(edit);
        if (IsBusy) return;

        string? error = null;
        DisplayImage? display = null;
        IsBusy = true;
        try
        {
            var source = Image;
            ImageBuffer next;
            (next, display) = await Task.Run(() =>
            {
                var edited = edit(source, CancellationToken.None)
                    ?? throw new InvalidOperationException("編集の結果がありません。");
                return (edited, DisplayImage.Create(edited));
            });
            History.Push(name, next, changedRegion);
        }
        catch (OutOfMemoryException ex)
        {
            _log.Error($"Edit OOM: {name} ({Path})", ex);
            error = Messages.M28_EditFailed(Messages.OutOfMemory);
        }
        catch (Exception ex)
        {
            // フィルターなどのプラグインの例外も、アプリを落とさない（設計書 5.10-6）
            _log.Error($"Edit failed: {name} ({Path})", ex);
            error = Messages.M28_EditFailed(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        if (error is not null)
        {
            _dialogs.ShowError(error);
            return;
        }

        // 画像が変わったので、選択範囲は新しい画像の内側に収め、表示用画像を差し替える
        Selection = newSelection;
        if (resetView) ResetViewRequest++;
        Display = display!;
        NotifyImageChanged();
    }

    /// <summary>今の画像（History.Current）が変わったことを知らせる（大きさ・書き出していない編集の印も変わりうる）</summary>
    private void NotifyImageChanged()
    {
        OnPropertyChanged(nameof(Image));
        OnPropertyChanged(nameof(ImageWidth));
        OnPropertyChanged(nameof(ImageHeight));
        OnPropertyChanged(nameof(HasUnexportedEdits));
        OnPropertyChanged(nameof(Title));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- 選択範囲（IEditorSession）

    private PixelRect? _selection;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public int ImageWidth => Image.Width;

    /// <inheritdoc />
    public int ImageHeight => Image.Height;

    /// <inheritdoc />
    public FrameRules SelectionRules
        => CropFrameCalculator.Rules(ImageWidth, ImageHeight, _selectionAspect(ImageWidth, ImageHeight));

    /// <inheritdoc />
    public PixelRect? Selection
    {
        get => _selection;
        set
        {
            _refitBase = null; // 比率の合わせ直しのほかで変えたら、それが次の合わせ直しの元になる
            var clamped = value is { } rect ? CropFrameCalculator.Clamp(rect, SelectionRules) : (PixelRect?)null;
            if (clamped == _selection) return;
            _selection = clamped;
            OnPropertyChanged();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 比率を変え始める前の選択範囲（比率を続けて変えている間、合わせ直しはいつもこれから行う。
    /// カスタムの比率を打つ途中の 1200:4 などで選択範囲が細くなり、次の比率で小さくなったままにならないように）
    /// </summary>
    private PixelRect? _refitBase;

    /// <summary>
    /// 選択範囲の比率（全画像共通の設定）が変わったときに MainViewModel が呼ぶ。選択範囲を新しい比率に合わせ直し
    /// （中心と面積をなるべく保つ。CropFrameCalculator.FitToAspect）、描き直しを知らせる（辺のハンドルの有無も変わるため）。
    /// 比率を続けて変えている間は、比率を変え始める前の選択範囲から合わせ直す。
    /// 自由にしたときは、今の選択範囲のまま（そのまま使える）にし、それを次の合わせ直しの元にする
    /// </summary>
    public void RefitSelection()
    {
        if (SelectionRules.Aspect is null)
            _refitBase = null;
        else if ((_refitBase ?? _selection) is { } basis)
        {
            Selection = CropFrameCalculator.FitToAspect(basis, SelectionRules);
            _refitBase = basis;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDisplayChanged(DisplayImage value) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>元ファイルのバイト数。読めなければ 0</summary>
    internal static long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
