using System.Collections.ObjectModel;
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
/// メインウィンドウの ViewModel（設計書 5.6）。画像の一覧と、選んでいる画像、表示中の画像（フル解像度）を持つ。
/// </summary>
/// <remarks>
/// 一覧は最初から「複数の画像と、選んでいる画像」の形にしてある。今は［開く］で 1 枚ずつ読み、一覧は 1 件まで
/// （開くと一覧を入れ替える）。複数の画像を扱うのは実装計画書 9-2 から。
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly FormatRegistry _formats;
    private readonly SettingsService _settingsService;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;
    private readonly TimeProvider _time;

    public MainViewModel(FormatRegistry formats, SettingsService settingsService, IDialogService dialogs, AppLog log,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);
        _formats = formats;
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
    /// ステータスバー: 「画像: {編集画像の幅}×{高さ} ／ {メッセージ}」（設計書 4.2）。
    /// 枠・書き出しの項目は、それぞれの機能を作るステップで足す
    /// </summary>
    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            // 回転・反転（7-1）ができるまでは、編集画像の大きさ = 向きを直した元画像の大きさ
            if (SourceImage is { } image) parts.Add($"画像: {image.Width}×{image.Height}");
            if (!string.IsNullOrEmpty(StatusMessage)) parts.Add(StatusMessage);
            return string.Join(" ／ ", parts);
        }
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
        Items.Clear();
        Items.Add(item);
        SelectedItem = item;
        StatusMessage = null;

        string? error = null;
        IsBusy = true;
        try
        {
            var (image, size) = await Task.Run(() =>
            {
                var decoded = _formats.Decode(item.Path);
                return (decoded.Image, ImageItemViewModel.GetFileSize(item.Path));
            });
            item.SetLoaded(size);
            SourceImage = image;
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
