using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Views.Canvas;

namespace WebDeza.ViewModels;

/// <summary>
/// 開いている画像 1 枚（設計書 5.6）。今の画像（フル解像度）・選択範囲・履歴・表示用画像を持ち、
/// キャンバスと道具の窓口（IEditorSession）になる。
/// </summary>
public sealed partial class DocumentViewModel : ObservableObject, IEditorSession
{
    private readonly Func<int, int, AspectRatio?> _selectionAspect;

    /// <summary>開いたとき（または最後に書き出したとき）の履歴の状態の番号</summary>
    private long _exportedStateId;

    /// <param name="path">元画像のフルパス</param>
    /// <param name="image">開いた画像（向きを直した Bgra32）</param>
    /// <param name="display">その表示用画像（バックグラウンドで作っておいたもの）</param>
    /// <param name="sourceFileSize">元ファイルのバイト数</param>
    /// <param name="selectionAspect">画像の幅・高さから、選択範囲の比率を返す（全画像共通の設定。null なら自由）</param>
    public DocumentViewModel(string path, ImageBuffer image, DisplayImage display, long sourceFileSize,
        Func<int, int, AspectRatio?> selectionAspect)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(selectionAspect);
        if (display.ImageWidth != image.Width || display.ImageHeight != image.Height)
            throw new ArgumentException("表示用画像が、画像と同じ大きさのものではありません。", nameof(display));

        Path = path;
        SourceFileSize = sourceFileSize;
        History = new ImageHistory(image);
        _display = display;
        _selectionAspect = selectionAspect;
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

    /// <summary>書き出していない編集があるか（履歴の状態が、開いたとき・最後に書き出したときと違う）</summary>
    public bool HasUnexportedEdits => History.StateId != _exportedStateId;

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
            var clamped = value is { } rect ? CropFrameCalculator.Clamp(rect, SelectionRules) : (PixelRect?)null;
            if (clamped == _selection) return;
            _selection = clamped;
            OnPropertyChanged();
            Changed?.Invoke(this, EventArgs.Empty);
        }
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
