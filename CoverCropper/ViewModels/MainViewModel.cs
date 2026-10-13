using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoverCropper.Models;
using CoverCropper.Services;

namespace CoverCropper.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IImageLoader _loader;
    private readonly IImageResizer _resizer;
    private readonly IImageCropper _cropper;
    private readonly IJpegSaver _saver;
    private readonly IOutputFileNamer _namer;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogs;

    public MainViewModel(
        IImageLoader loader,
        IImageResizer resizer,
        IImageCropper cropper,
        IJpegSaver saver,
        IOutputFileNamer namer,
        ISettingsService settingsService,
        IDialogService dialogs)
    {
        _loader = loader;
        _resizer = resizer;
        _cropper = cropper;
        _saver = saver;
        _namer = namer;
        _settingsService = settingsService;
        _dialogs = dialogs;

        Settings = settingsService.Load();
        OutputFolder = Settings.OutputFolder;
        if (settingsService.LastLoadError is not null)
            Notice = Messages.SettingsLoadFailed;
    }

    /// <summary>永続化される設定（ウィンドウ位置の保存にも使う）</summary>
    public AppSettings Settings { get; }

    /// <summary>現在読み込んでいる画像の情報（未読み込み時 null）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(StatusText), nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(ResetPositionCommand))]
    public partial CropState? Current { get; private set; }

    /// <summary>リサイズ後の画像（メモリ上のみ。ファイルには保存しない）</summary>
    [ObservableProperty]
    public partial BitmapSource? ResizedImage { get; private set; }

    /// <summary>切り抜き枠の X（リサイズ後画像のピクセル座標）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int CropX { get; set; }

    /// <summary>切り抜き枠の Y（リサイズ後画像のピクセル座標）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int CropY { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputFolderDisplay))]
    [NotifyCanExecuteChangedFor(nameof(OpenOutputFolderCommand))]
    public partial string? OutputFolder { get; private set; }

    /// <summary>ステータスバーに表示する一時的なメッセージ</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? Notice { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(SaveCommand), nameof(ResetPositionCommand),
        nameof(ChangeOutputFolderCommand))]
    public partial bool IsBusy { get; private set; }

    public bool HasImage => Current is not null;

    public string OutputFolderDisplay => string.IsNullOrEmpty(OutputFolder) ? Messages.NotSet : OutputFolder;

    public string WindowTitle => Current is null
        ? "CoverCropper"
        : $"{Path.GetFileName(Current.SourcePath)} - CoverCropper";

    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            if (Current is { } c)
            {
                parts.Add($"元: {c.OriginalWidth}×{c.OriginalHeight} → リサイズ後: {c.ResizedWidth}×{c.ResizedHeight}");
                parts.Add($"切り抜き位置: ({CropX}, {CropY})");
                if (c.IsUpscaled) parts.Add(Messages.M03_Upscaled);
            }
            if (!string.IsNullOrEmpty(Notice)) parts.Add(Notice);
            return string.Join(" ／ ", parts);
        }
    }

    private bool CanEditImage() => HasImage && !IsBusy;
    private bool IsIdle() => !IsBusy;

    // ---------------------------------------------------------------- 読み込み

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenAsync()
    {
        var path = _dialogs.ShowOpenImageDialog(Settings.LastOpenFolder);
        if (path is not null)
            await LoadAsync(path);
    }

    /// <summary>D&amp;D されたパス群を処理する（先頭の1件のみ）</summary>
    public async Task DropAsync(IReadOnlyList<string> paths)
    {
        if (IsBusy || paths.Count == 0) return;
        await LoadAsync(paths[0], multipleDropped: paths.Count > 1);
    }

    /// <summary>D&amp;D の受け入れ可否（先頭が対応拡張子のファイルか）</summary>
    public bool CanAcceptDrop(IReadOnlyList<string>? paths)
        => !IsBusy && paths is { Count: > 0 } && File.Exists(paths[0]) && AppConstants.IsSupportedFile(paths[0]);

    public async Task LoadAsync(string path, bool multipleDropped = false)
    {
        if (!File.Exists(path) || !AppConstants.IsSupportedFile(path))
        {
            _dialogs.ShowError(Messages.M01_UnsupportedFormat);
            return;
        }

        IsBusy = true;
        try
        {
            var (state, resized) = await Task.Run(() =>
            {
                var source = _loader.Load(path);
                var (w, h) = _resizer.CalculateSize(source.PixelWidth, source.PixelHeight);
                var image = _resizer.Resize(source, w, h);
                return (new CropState(path, source.PixelWidth, source.PixelHeight, w, h), image);
            });

            ResizedImage = resized;
            Current = state;
            (CropX, CropY) = _cropper.Center(state.ResizedWidth, state.ResizedHeight);
            Notice = multipleDropped ? Messages.M08_FirstFileOnly : null;

            Settings.LastOpenFolder = Path.GetDirectoryName(path);
            SaveSettingsQuietly();
        }
        catch (NotSupportedException ex)
        {
            // 拡張子は対応でも、デコーダ（WIC コーデック）が無い・中身が別形式の場合
            AppLog.Error($"Load not supported: {path}", ex);
            _dialogs.ShowError(Messages.M01_UnsupportedFormat);
        }
        catch (OutOfMemoryException ex)
        {
            AppLog.Error($"Load OOM: {path}", ex);
            _dialogs.ShowError(Messages.M02_LoadFailed(Messages.OutOfMemory));
        }
        catch (Exception ex)
        {
            AppLog.Error($"Load failed: {path}", ex);
            _dialogs.ShowError(Messages.M02_LoadFailed(ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------------------------------------------------- 枠操作

    [RelayCommand(CanExecute = nameof(CanEditImage))]
    private void ResetPosition()
    {
        if (Current is null) return;
        (CropX, CropY) = _cropper.Center(Current.ResizedWidth, Current.ResizedHeight);
    }

    /// <summary>枠を相対移動する（画像内にクランプ）</summary>
    public void Move(int dx, int dy)
    {
        if (Current is null || IsBusy) return;
        (CropX, CropY) = _cropper.Clamp(CropX + dx, CropY + dy, Current.ResizedWidth, Current.ResizedHeight);
    }

    // ---------------------------------------------------------------- 保存

    [RelayCommand(CanExecute = nameof(CanEditImage))]
    private async Task SaveAsync()
    {
        if (Current is null || ResizedImage is null) return;

        var folder = EnsureOutputFolder();
        if (folder is null) return;

        var state = Current;
        var image = ResizedImage;
        var (x, y) = _cropper.Clamp(CropX, CropY, state.ResizedWidth, state.ResizedHeight);

        IsBusy = true;
        try
        {
            var savedPath = await Task.Run(() =>
            {
                var path = _namer.CreatePath(folder, state.SourcePath, DateTime.Now);

                // 安全装置: 元画像のパスには絶対に書き込まない
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(state.SourcePath),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Messages.SameAsSourcePath);

                var cropped = _cropper.Crop(image, x, y);
                _saver.Save(cropped, path, AppConstants.JpegQuality);
                return path;
            });

            Notice = Messages.M04_Saved(Path.GetFileName(savedPath));
        }
        catch (Exception ex)
        {
            AppLog.Error($"Save failed: {state.SourcePath}", ex);
            _dialogs.ShowError(Messages.M07_SaveFailed(ex is OutOfMemoryException ? Messages.OutOfMemory : ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>保存先フォルダを確定する。未設定・存在しない場合はユーザーに選択させる。</summary>
    private string? EnsureOutputFolder()
    {
        var folder = OutputFolder;

        if (string.IsNullOrEmpty(folder))
        {
            if (!_dialogs.ConfirmOkCancel(Messages.M05_OutputFolderNotSet)) return null;
            return SelectOutputFolder(null);
        }

        if (!Directory.Exists(folder))
        {
            _dialogs.ShowError(Messages.M06_OutputFolderNotFound(folder));
            if (!_dialogs.ConfirmYesNo(Messages.AskChangeOutputFolder)) return null;
            return SelectOutputFolder(folder);
        }

        return folder;
    }

    // ---------------------------------------------------------------- 保存先設定

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ChangeOutputFolder() => SelectOutputFolder(OutputFolder);

    private bool CanOpenOutputFolder() => !string.IsNullOrEmpty(OutputFolder);

    [RelayCommand(CanExecute = nameof(CanOpenOutputFolder))]
    private void OpenOutputFolder()
    {
        if (string.IsNullOrEmpty(OutputFolder)) return;
        if (!Directory.Exists(OutputFolder))
        {
            _dialogs.ShowError(Messages.M06_OutputFolderNotFound(OutputFolder));
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{OutputFolder}\"") { UseShellExecute = true });
    }

    private string? SelectOutputFolder(string? initial)
    {
        var selected = _dialogs.ShowFolderDialog(initial);
        if (selected is null) return null;

        OutputFolder = selected;
        Settings.OutputFolder = selected;
        SaveSettingsQuietly();
        return selected;
    }

    public void SaveSettingsQuietly()
    {
        try
        {
            _settingsService.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Settings save failed", ex);
            Notice = $"設定を保存できませんでした（{ex.Message}）";
        }
    }
}
