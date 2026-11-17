using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebDeza.Core;
using WebDeza.Imaging.Editing;
using WebDeza.Services;

namespace WebDeza.ViewModels;

/// <summary>
/// 保存のダイアログ（設計書 F-08・F-10・4.6）。アクティブな画像の今の画像（編集を確定した結果の全体）を、
/// 保存の設定で出力先に保存する。保存の処理はダイアログの中で行い、失敗したときはダイアログを閉じない。
/// </summary>
/// <remarks>
/// 出力先は［変更…］で選び直せ、［開く］でエクスプローラーに出す。変えた出力先と設定は、保存できたときだけ
/// MainViewModel が設定に記録する（Saved を受けて）。［キャンセル］では何も記録しない（設計書 F-08）。
/// 形式・オプション・背景色の入力欄は 10-3 以降で足す（今は、渡された保存の設定のまま保存する）。
/// </remarks>
public sealed partial class ExportDialogViewModel : ObservableObject
{
    private readonly DocumentViewModel _document;
    private readonly ExportService _exporter;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;

    /// <param name="document">保存する画像</param>
    /// <param name="outputFolder">出力先（設定のもの。未設定なら null）</param>
    /// <param name="settings">保存の設定（前回［保存］を押したときのもの）</param>
    /// <param name="exporter">保存の処理</param>
    /// <param name="dialogs">メッセージとフォルダの選択</param>
    /// <param name="log">保存に失敗したときに書くログ</param>
    public ExportDialogViewModel(DocumentViewModel document, string? outputFolder, ExportSettings settings,
        ExportService exporter, IDialogService dialogs, AppLog log)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(log);
        _document = document;
        _exporter = exporter;
        _dialogs = dialogs;
        _log = log;
        _outputFolder = string.IsNullOrWhiteSpace(outputFolder) ? null : outputFolder;
        Settings = settings;
        RefreshFileName();
    }

    /// <summary>保存する画像</summary>
    public DocumentViewModel Document => _document;

    /// <summary>ダイアログのタイトル: 「保存: {ファイル名}」</summary>
    public string Title => $"保存: {_document.FileName}";

    /// <summary>保存の設定（保存できたら、MainViewModel が設定に記録する）</summary>
    public ExportSettings Settings { get; }

    /// <summary>保存する大きさ（今の画像の大きさのまま保存する。設計書 F-08）</summary>
    public string SizeText => $"{_document.ImageWidth} × {_document.ImageHeight} px";

    /// <summary>保存する形式の名前（形式の選択は 10-3 で足す）</summary>
    public string FormatText
    {
        get
        {
            try
            {
                return _exporter.GetEncodableFormat(Settings.FormatId).DisplayName;
            }
            catch (NotSupportedException)
            {
                return Settings.FormatId;
            }
        }
    }

    /// <summary>出力先のフォルダ（未設定なら null）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputFolderText))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    private string? _outputFolder;

    partial void OnOutputFolderChanged(string? value) => RefreshFileName();

    /// <summary>出力先の表示（未設定なら「（未設定）」）</summary>
    public string OutputFolderText => OutputFolder ?? "（未設定）";

    /// <summary>
    /// 保存するファイル名（設計書 F-08）。日時は、ダイアログを開いたとき（出力先を変えたとき）のもの。
    /// 実際の名前は、保存したときの日時になる
    /// </summary>
    [ObservableProperty]
    private string _fileName = "";

    /// <summary>保存の処理をしているか（このあいだは、ボタンを無効にし、ダイアログを閉じさせない）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(ChangeFolderCommand))]
    private bool _isSaving;

    /// <summary>保存の処理をしていないか（［キャンセル］を押せるか）</summary>
    public bool IsIdle => !IsSaving;

    /// <summary>保存した結果（保存できたときだけ）</summary>
    public ExportResult? Result { get; private set; }

    /// <summary>保存できた（MainViewModel が受けて、設定に記録し、ステータスバーに M04 を出す）</summary>
    public event EventHandler<ExportResult>? Saved;

    /// <summary>ダイアログを閉じるように頼む（保存できたとき）</summary>
    public event EventHandler? CloseRequested;

    private void RefreshFileName()
    {
        try
        {
            FileName = _exporter.CreateOutputFileName(OutputFolder, _document.Path, Settings.FormatId);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            FileName = "";
        }
    }

    // ---------------------------------------------------------------- 出力先

    private bool CanChangeFolder() => !IsSaving;

    /// <summary>［変更…］: 出力先をフォルダのダイアログで選ぶ（やめたら、そのまま）</summary>
    [RelayCommand(CanExecute = nameof(CanChangeFolder))]
    private void ChangeFolder()
    {
        if (_dialogs.ShowFolderDialog(OutputFolder) is { } selected) OutputFolder = selected;
    }

    private bool CanOpenFolder() => OutputFolder is not null;

    /// <summary>［開く］: 出力先をエクスプローラーで開く（無ければ M06）</summary>
    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        if (OutputFolder is not { } folder) return;
        if (!Directory.Exists(folder))
        {
            _dialogs.ShowError(Messages.M06_OutputFolderNotFound(folder));
            return;
        }
        _dialogs.OpenFolder(folder);
    }

    /// <summary>
    /// 保存する出力先を決める（CoverCropper と同じ）: 未設定なら M05 を出してフォルダを選ばせる（選んだら、それを出力先にして続ける）。
    /// 無ければ M06 を出してやめる（ダイアログは閉じないので、［変更…］で選び直せる）
    /// </summary>
    private string? EnsureOutputFolder()
    {
        if (OutputFolder is null)
        {
            if (!_dialogs.ConfirmOkCancel(Messages.M05_OutputFolderNotSet)) return null;
            if (_dialogs.ShowFolderDialog(null) is not { } selected) return null;
            OutputFolder = selected;
        }

        if (!Directory.Exists(OutputFolder))
        {
            _dialogs.ShowError(Messages.M06_OutputFolderNotFound(OutputFolder!));
            return null;
        }
        return OutputFolder;
    }

    // ---------------------------------------------------------------- 保存（設計書 F-10）

    private bool CanSave() => !IsSaving;

    /// <summary>
    /// ［保存］: 今の画像を保存の設定で出力先に保存する（バックグラウンド）。保存できたら、その画像の
    /// 「保存していない編集」の印を消し、Saved を出してダイアログを閉じる。失敗したら M07 を出し、ダイアログは閉じない
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (IsSaving || EnsureOutputFolder() is not { } folder) return;

        var image = _document.Image;          // 編集を確定した今の画像（フィルターのプレビューは含まない）
        long stateId = _document.History.StateId;
        var sourcePath = _document.Path;
        var settings = Settings;

        ExportResult? result = null;
        string? error = null;
        IsSaving = true;
        try
        {
            result = await Task.Run(() => _exporter.Export(image, sourcePath, folder, settings));
        }
        catch (DirectoryNotFoundException ex)
        {
            // 確かめた後にフォルダが消えた
            _log.Error($"Export folder not found: {folder}", ex);
            error = Messages.M06_OutputFolderNotFound(folder);
        }
        catch (OutOfMemoryException ex)
        {
            _log.Error($"Export OOM: {sourcePath}", ex);
            error = Messages.M07_ExportFailed(Messages.OutOfMemory);
        }
        catch (Exception ex)
        {
            // 書けない・同じ名前ができた・元画像と同じパス・形式プラグインの例外など（設計書 5.10-6）
            _log.Error($"Export failed: {sourcePath} -> {folder}", ex);
            error = Messages.M07_ExportFailed(ex.Message);
        }
        finally
        {
            IsSaving = false;
        }

        if (result is null)
        {
            _dialogs.ShowError(error!);
            RefreshFileName();
            return;
        }

        Result = result;
        _document.MarkExported(stateId);
        Saved?.Invoke(this, result);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ステータスバーに出す M04（「保存しました: {ファイル名}（{サイズ}）」）</summary>
    public static string SavedMessage(ExportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Messages.M04_Exported(Path.GetFileName(result.Path), FileSizeFormatter.Format(result.FileSize));
    }
}
