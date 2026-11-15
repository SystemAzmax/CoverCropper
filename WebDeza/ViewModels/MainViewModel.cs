using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Formats;
using WebDeza.Models;
using WebDeza.Services;

namespace WebDeza.ViewModels;

/// <summary>
/// メインウィンドウの ViewModel（設計書 5.6）。開いている画像の一覧（Documents）とアクティブな画像を持つ。
/// </summary>
/// <remarks>
/// 一覧は最初から「複数の画像と、アクティブな画像」の形にしてある。今は［開く］で 1 枚ずつ開き、一覧は 1 件まで
/// （開くと入れ替える）。複数の画像を開けるようにするのは実装計画書 7-3 から。
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly FormatRegistry _formats;
    private readonly SettingsService _settingsService;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;

    public MainViewModel(FormatRegistry formats, SettingsService settingsService, IDialogService dialogs, AppLog log)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);
        _formats = formats;
        _settingsService = settingsService;
        _dialogs = dialogs;
        _log = log;

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

    /// <summary>開いている画像（開いた順）</summary>
    public ObservableCollection<DocumentViewModel> Documents { get; } = new();

    /// <summary>アクティブな画像（メニュー・道具・編集パネル・ステータスバーの対象）。画像が無ければ null</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(WindowTitle), nameof(StatusText))]
    private DocumentViewModel? _activeDocument;

    /// <summary>読み込み中・編集の確定中（このあいだは開くなどを無効にし、カーソルを待機にする）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(ExitCommand))]
    private bool _isBusy;

    /// <summary>ステータスバーの末尾に出すメッセージ</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _statusMessage;

    public bool HasImage => ActiveDocument is not null;

    /// <summary>ウィンドウのタイトル: 「{アクティブな画像のファイル名} - webDeza」（画像が無いときは「webDeza」）</summary>
    public string WindowTitle => ActiveDocument is { } document
        ? $"{document.FileName} - {AppInfo.ProductName}"
        : AppInfo.ProductName;

    /// <summary>
    /// ステータスバー（設計書 4.2）: 「画像: {幅}×{高さ} ／ 選択範囲: ({X}, {Y}) {幅}×{高さ} ／ {メッセージ}」。
    /// 選択範囲が無いときは、その項目を出さない。倍率は、ズームを作るステップ（8-2）で足す
    /// </summary>
    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            if (ActiveDocument is { } document)
            {
                parts.Add($"画像: {document.ImageWidth}×{document.ImageHeight}");
                if (document.Selection is { } s) parts.Add($"選択範囲: ({s.X}, {s.Y}) {s.Width}×{s.Height}");
            }
            if (!string.IsNullOrEmpty(StatusMessage)) parts.Add(StatusMessage);
            return string.Join(" ／ ", parts);
        }
    }

    partial void OnActiveDocumentChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnActiveDocumentPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnActiveDocumentPropertyChanged;
    }

    private void OnActiveDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 選択範囲・画像が変わったら、ステータスバーを更新する
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>選択範囲の比率（全画像共通の設定）を、画像の大きさに当てはめたもの</summary>
    private AspectRatio? SelectionAspect(int imageWidth, int imageHeight)
        => Settings.SelectionAspect.ToAspectRatio(imageWidth, imageHeight);

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
    /// 画像を 1 枚開く。読み込みと表示用画像の作成はバックグラウンドで行う。
    /// 読めない形式は M01（要るものがあれば M21 を続ける）、そのほかの失敗は M02。読めなかったときは何も変えない。
    /// </summary>
    /// <remarks>今は一覧を 1 件までにしているので、開けたら前の画像と入れ替える（7-3 で、足していく形にする）</remarks>
    public async Task LoadAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (IsBusy) return;

        if (!_formats.CanDecode(path))
        {
            _dialogs.ShowError(Messages.M01_UnsupportedFormat);
            return;
        }

        var fullPath = Path.GetFullPath(path);
        StatusMessage = null;

        DocumentViewModel? document = null;
        string? error = null;
        IsBusy = true;
        try
        {
            var (image, display, size) = await Task.Run(() =>
            {
                var decoded = _formats.Decode(fullPath);
                var display = DisplayImage.Create(decoded.Image);
                return (decoded.Image, display, DocumentViewModel.GetFileSize(fullPath));
            });
            document = new DocumentViewModel(fullPath, image, display, size, SelectionAspect);
        }
        catch (UnsupportedImageFormatException ex)
        {
            // 拡張子は対応していても、中身が読めない・OS のコーデック（拡張機能）が無い
            _log.Error($"Load not supported: {fullPath}", ex);
            error = ex.Requirement is { } requirement
                ? $"{Messages.M01_UnsupportedFormat}\n{Messages.M21_Requirement(requirement)}"
                : Messages.M01_UnsupportedFormat;
        }
        catch (NotSupportedException ex)
        {
            _log.Error($"Load not supported: {fullPath}", ex);
            error = Messages.M01_UnsupportedFormat;
        }
        catch (OutOfMemoryException ex)
        {
            _log.Error($"Load OOM: {fullPath}", ex);
            error = Messages.M02_LoadFailed(Messages.OutOfMemory);
        }
        catch (Exception ex)
        {
            // 壊れたファイル・大きすぎる画像・形式プラグインの例外など（アプリは落とさない。設計書 5.10-6）
            _log.Error($"Load failed: {fullPath}", ex);
            error = Messages.M02_LoadFailed(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        Settings.LastOpenFolder = Path.GetDirectoryName(fullPath);
        SaveSettingsQuietly();

        if (document is not null)
        {
            // 前の画像への参照を外してから、新しい画像にする（一覧は 1 件まで）
            ActiveDocument = null;
            Documents.Clear();
            Documents.Add(document);
            ActiveDocument = document;
        }
        else if (error is not null)
        {
            _dialogs.ShowError(error);
        }
    }

    // ---------------------------------------------------------------- 終了

    /// <summary>終了を頼まれた（メインウィンドウが受けて閉じる。閉じるときの処理はウィンドウの Closing に集める）</summary>
    public event EventHandler? ExitRequested;

    private bool CanExit => !IsBusy;

    /// <summary>メニュー［ファイル］→［終了］（設計書 F-13。M17 の確認は 9-8 で足す）</summary>
    [RelayCommand(CanExecute = nameof(CanExit))]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

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
