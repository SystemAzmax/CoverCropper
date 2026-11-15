using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Models;
using WebDeza.Services;
using WebDeza.Views.Canvas;

namespace WebDeza.ViewModels;

/// <summary>
/// メインウィンドウの ViewModel（設計書 5.6）。画像の一覧と、選んでいる画像、表示中の画像（フル解像度）を持つ。
/// </summary>
/// <remarks>
/// 一覧は最初から「複数の画像と、選んでいる画像」の形にしてある。今は［開く］で 1 枚ずつ読み、一覧は 1 件まで
/// （開くと一覧を入れ替える）。複数の画像を扱うのは実装計画書 9-2 から。
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IEditorSession
{
    /// <summary>表示用画像の長辺の上限（設計書 F-03）。これより大きい画像は縮小して表示する（書き出しには使わない）</summary>
    public const int PreviewMaxSide = 4096;

    private readonly FormatRegistry _formats;
    private readonly RenderPipeline _pipeline;
    private readonly SettingsService _settingsService;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;
    private readonly TimeProvider _time;

    public MainViewModel(FormatRegistry formats, FilterRegistry filters, SettingsService settingsService,
        IDialogService dialogs, AppLog log, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);
        _formats = formats;
        _pipeline = new RenderPipeline(filters);
        _settingsService = settingsService;
        _dialogs = dialogs;
        _log = log;
        _time = time ?? TimeProvider.System;

        var loaded = settingsService.Load();
        Settings = loaded.Settings;
        if (loaded.Messages.Count > 0)
        {
            StatusMessage = string.Join(" ／ ", loaded.Messages);
            _log.Write($"Settings: {string.Join(" / ", loaded.Messages)} ({settingsService.FilePath})");
        }
    }

    /// <summary>検証済みの設定（変えたら SaveSettingsQuietly で保存する）</summary>
    public AppSettings Settings { get; }

    /// <summary>画像の一覧</summary>
    public ObservableCollection<ImageItemViewModel> Items { get; } = new();

    /// <summary>選んでいる画像</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private ImageItemViewModel? _selectedItem;

    /// <summary>表示中の画像（向きを直した元画像。フル解像度）。読み込めていなければ null</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(WindowTitle), nameof(StatusText))]
    private ImageBuffer? _sourceImage;

    /// <summary>キャンバスに描く表示用画像。読み込めていなければ null</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(CropFrame))]
    private DisplayImage? _display;

    /// <summary>
    /// キャンバスを全体表示に戻す合図。画像を切り替えたとき（7-1 からは 90° 回転したときも）に 1 増やす。
    /// ズームの状態は表示だけの関心事なので ViewModel には持たせない（設計書 5.6）
    /// </summary>
    [ObservableProperty]
    private int _resetViewRequest;

    /// <summary>読み込み中・書き出し中（このあいだは開くなどを無効にし、カーソルを待機にする）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private bool _isBusy;

    /// <summary>ステータスバーの末尾に出すメッセージ</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _statusMessage;

    public bool HasImage => SourceImage is not null;

    /// <summary>ウィンドウのタイトル: 「{ファイル名} - webDeza」（画像が無いときは「webDeza」）</summary>
    public string WindowTitle => HasImage && SelectedItem is { } item
        ? $"{item.FileName} - {AppInfo.ProductName}"
        : AppInfo.ProductName;

    /// <summary>
    /// ステータスバー（設計書 4.2）:
    /// 「画像: {編集画像の幅}×{高さ} ／ 枠: ({X}, {Y}) {幅}×{高さ} ／ 書き出し: {幅}×{高さ} {形式}（{書き出し倍率} 倍）／ {メッセージ}」。
    /// 書き出し倍率が 1 を超えるときは M03 を出す。書き出しサイズ（バイト数）は、計算を作るステップで足す
    /// </summary>
    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            if (Display is { } display && CropFrame is { } frame)
            {
                parts.Add($"画像: {display.EditedWidth}×{display.EditedHeight}");
                parts.Add($"枠: ({frame.X}, {frame.Y}) {frame.Width}×{frame.Height}");

                var outputSize = OutputSize;
                var (width, height) = CropFrameCalculator.OutputPixelSize(frame, outputSize);
                double scale = CropFrameCalculator.OutputScale(frame, outputSize);
                string format = _formats.FindById(Settings.Export.FormatId)?.DisplayName ?? Settings.Export.FormatId;
                parts.Add($"書き出し: {width}×{height} {format}（{scale.ToString("0.00", CultureInfo.InvariantCulture)} 倍）");
                if (scale > 1) parts.Add(Messages.M03_Upscaled);
            }
            if (!string.IsNullOrEmpty(StatusMessage)) parts.Add(StatusMessage);
            return string.Join(" ／ ", parts);
        }
    }

    // ---------------------------------------------------------------- 切り抜き（IEditorSession）

    /// <summary>ドラッグ中の途中の状態（Preview で渡されたもの）。確定したら null</summary>
    private EditRecipe? _previewRecipe;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public EditRecipe Recipe => _previewRecipe ?? SelectedItem?.History.Current ?? EditRecipe.Initial;

    /// <inheritdoc />
    public int EditedWidth => Display?.EditedWidth ?? 0;

    /// <inheritdoc />
    public int EditedHeight => Display?.EditedHeight ?? 0;

    /// <inheritdoc />
    public OutputSize OutputSize => Settings.Export.Size.ToOutputSize();

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">画像が無い</exception>
    public FrameRules CropRules => Display is { } display
        ? CropFrameCalculator.Rules(display.EditedWidth, display.EditedHeight, CurrentCropAspect, OutputSize)
        : throw new InvalidOperationException("表示している画像がありません。");

    /// <summary>
    /// 今の枠（編集画像のピクセル座標）。レシピの枠が null なら初期状態の枠、あれば規則に合わせたもの。画像が無ければ null
    /// </summary>
    public PixelRect? CropFrame => Display is null ? null : this.GetCropFrame();

    /// <summary>切り抜きの比率の選択（設定は検証済みなので読めるはずだが、読めなければ自由）</summary>
    private CropAspect CurrentCropAspect => Settings.CropAspect.ToCropAspect() ?? CropAspect.Free;

    /// <inheritdoc />
    public void Preview(EditRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (SelectedItem is null || Display is null) throw new InvalidOperationException("表示している画像がありません。");
        _previewRecipe = recipe;
        OnRecipeChanged();
    }

    /// <inheritdoc />
    public void Commit(EditRecipe recipe, string? coalesceKey = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (SelectedItem is not { } item || Display is null) throw new InvalidOperationException("表示している画像がありません。");
        _previewRecipe = null;
        item.History.Commit(recipe, coalesceKey);
        OnRecipeChanged();
    }

    /// <summary>レシピが変わった（枠とステータスバーを更新し、キャンバスに描き直させる）</summary>
    private void OnRecipeChanged()
    {
        OnPropertyChanged(nameof(CropFrame));
        OnPropertyChanged(nameof(StatusText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDisplayChanged(DisplayImage? value)
    {
        _previewRecipe = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- 開く

    private bool CanOpen => !IsBusy;

    /// <summary>［開く］ / Ctrl+O</summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync()
    {
        var path = _dialogs.ShowOpenImageDialog(Settings.LastOpenFolder, OpenFileFilter.Build(_formats));
        if (path is null) return;
        await LoadAsync(path);
    }

    /// <summary>
    /// 画像を 1 枚読み込んで表示する。読み込みはバックグラウンドで行う。
    /// 読めない形式は M01（要るものがあれば M21 を続ける）、そのほかの失敗は M02。失敗した画像も一覧に残し、状態をエラーにする
    /// </summary>
    public async Task LoadAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (IsBusy) return;

        if (!_formats.CanDecode(path))
        {
            _dialogs.ShowError(Messages.M01_UnsupportedFormat);
            return;
        }

        var item = new ImageItemViewModel(Path.GetFullPath(path), _time);

        // 次の画像を読む前に、前の画像への参照を外す（大きな画像を 2 枚同時に持たないため。設計書 5.10-3）
        SourceImage = null;
        Display = null;
        Items.Clear();
        Items.Add(item);
        SelectedItem = item;
        StatusMessage = null;

        string? error = null;
        IsBusy = true;
        try
        {
            var recipe = item.History.Current;
            var aspect = CurrentCropAspect;
            var export = Settings.Export.ToExportSettings();
            var (image, display, size) = await Task.Run(() =>
            {
                var decoded = _formats.Decode(item.Path);
                var display = CreateDisplay(decoded.Image, recipe, aspect, export);
                return (decoded.Image, display, ImageItemViewModel.GetFileSize(item.Path));
            });
            item.SetLoaded(size);
            SourceImage = image;
            Display = display;
            ResetViewRequest++;
        }
        catch (UnsupportedImageFormatException ex)
        {
            // 拡張子は対応していても、中身が読めない・OS のコーデック（拡張機能）が無い
            _log.Error($"Load not supported: {item.Path}", ex);
            error = ex.Requirement is { } requirement
                ? $"{Messages.M01_UnsupportedFormat}\n{Messages.M21_Requirement(requirement)}"
                : Messages.M01_UnsupportedFormat;
        }
        catch (NotSupportedException ex)
        {
            _log.Error($"Load not supported: {item.Path}", ex);
            error = Messages.M01_UnsupportedFormat;
        }
        catch (OutOfMemoryException ex)
        {
            _log.Error($"Load OOM: {item.Path}", ex);
            error = Messages.M02_LoadFailed(Messages.OutOfMemory);
        }
        catch (Exception ex)
        {
            // 壊れたファイル・大きすぎる画像・形式プラグインの例外など（アプリは落とさない。設計書 5.10-6）
            _log.Error($"Load failed: {item.Path}", ex);
            error = Messages.M02_LoadFailed(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        Settings.LastOpenFolder = Path.GetDirectoryName(item.Path);
        SaveSettingsQuietly();

        if (error is not null)
        {
            item.SetError(error);
            _dialogs.ShowError(error);
        }
    }

    /// <summary>
    /// 表示用画像を作る: 向き → 長辺 4096 px 以下に縮小 → フィルター → 描画用の BitmapSource（設計書 5.8「表示」）。
    /// バックグラウンドで呼んでよい
    /// </summary>
    private DisplayImage CreateDisplay(ImageBuffer source, EditRecipe recipe, CropAspect aspect, ExportSettings export)
    {
        var preview = _pipeline.RenderPreview(source, recipe, aspect, export, PreviewMaxSide);
        return new DisplayImage(preview, BitmapSourceConverter.ToBitmapSource(preview.Image));
    }

    // ---------------------------------------------------------------- 設定

    /// <summary>設定を保存する。失敗してもアプリは止めず、ログに書く</summary>
    public void SaveSettingsQuietly()
    {
        try
        {
            _settingsService.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Settings save failed", ex);
        }
    }
}
