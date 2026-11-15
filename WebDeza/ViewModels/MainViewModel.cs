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
/// 開いた画像は Documents に足していき、画像ごとに子ウィンドウが 1 つできる（設計書 F-01・F-02）。
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>同時に開ける画像の数（設計書 F-01）</summary>
    public const int MaxDocuments = 50;

    private readonly FormatRegistry _formats;
    private readonly SettingsService _settingsService;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;
    private readonly FileCollector _collector;

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
        _collector = new FileCollector(formats);

        var loaded = settingsService.Load();
        Settings = loaded.Settings;
        if (loaded.Messages.Count > 0)
        {
            StatusMessage = string.Join(" ／ ", loaded.Messages);
            _log.Write($"Settings: {string.Join(" / ", loaded.Messages)} ({settingsService.FilePath})");
        }

        Documents.CollectionChanged += (_, _) => NotifyDocumentCommandsChanged();
    }

    /// <summary>検証済みの設定（変えたら SaveSettingsQuietly で保存する）</summary>
    public AppSettings Settings { get; }

    /// <summary>開いている画像（開いた順）</summary>
    public ObservableCollection<DocumentViewModel> Documents { get; } = new();

    /// <summary>アクティブな画像（メニュー・道具・編集パネル・ステータスバーの対象）。画像が無ければ null</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(WindowTitle), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private DocumentViewModel? _activeDocument;

    /// <summary>読み込み中・編集の確定中（このあいだは開くなどを無効にし、カーソルを待機にする）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(CloseCommand), nameof(ExitCommand))]
    private bool _isBusy;

    partial void OnIsBusyChanged(bool value) => NotifyDocumentCommandsChanged();

    /// <summary>開いている画像の数で有効・無効が決まるコマンドに、変わったことを知らせる</summary>
    private void NotifyDocumentCommandsChanged()
    {
        NextDocumentCommand.NotifyCanExecuteChanged();
        PreviousDocumentCommand.NotifyCanExecuteChanged();
        ActivateDocumentCommand.NotifyCanExecuteChanged();
        CloseAllCommand.NotifyCanExecuteChanged();
        CascadeWindowsCommand.NotifyCanExecuteChanged();
        TileWindowsCommand.NotifyCanExecuteChanged();
    }

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
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnActiveDocumentPropertyChanged;
            oldValue.IsActive = false;
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnActiveDocumentPropertyChanged;
            newValue.IsActive = true;
        }
    }

    private void OnActiveDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 選択範囲・画像が変わったら、ステータスバーを更新する（子ウィンドウの位置やアクティブの印が変わっただけのときは要らない）
        if (e.PropertyName is nameof(DocumentViewModel.WindowBounds) or nameof(DocumentViewModel.IsActive)) return;
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>選択範囲の比率（全画像共通の設定）を、画像の大きさに当てはめたもの</summary>
    private AspectRatio? SelectionAspect(int imageWidth, int imageHeight)
        => Settings.SelectionAspect.ToAspectRatio(imageWidth, imageHeight);

    // ---------------------------------------------------------------- 開く

    private bool CanOpen => !IsBusy;

    /// <summary>［開く］ / Ctrl+O（複数を選べる）。選んだフォルダを、次に「開く」で最初に出すフォルダとして覚える</summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync()
    {
        var paths = _dialogs.ShowOpenImageDialog(Settings.LastOpenFolder, OpenFileFilter.Build(_formats));
        if (paths is not { Count: > 0 }) return;

        Settings.LastOpenFolder = Path.GetDirectoryName(Path.GetFullPath(paths[0]));
        SaveSettingsQuietly();
        await OpenPathsAsync(paths);
    }

    /// <summary>画像を 1 枚開く（OpenPathsAsync の 1 件の場合）</summary>
    public Task LoadAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenPathsAsync([path]);
    }

    /// <summary>
    /// 画像を開く（設計書 F-01）。「開く」・D&amp;D・コマンドライン引数の共通の入り口で、ファイルとフォルダを受け取る。
    /// 開くファイルは FileCollector で決め（フォルダ直下の対応ファイル・重複は 1 つ・フルパスの自然順・上限まで）、
    /// その順に 1 枚ずつ、読み込みと表示用画像の作成をバックグラウンドで行い、Documents に足していく。
    /// 終わったら、今回の中で（自然順の）最後の画像をアクティブにする。既に開いている画像は開き直さず、選ぶだけにする。
    /// </summary>
    /// <remarks>
    /// 知らせ方:
    /// <list type="bullet">
    /// <item>新しく開いた（または非対応を除いた）ときは、ステータスバーに M09（「{n} 件を開きました。」。除いたものがあれば件数を続ける）</item>
    /// <item>同時に開ける数を超えた分は開かず、ステータスバーに M15</item>
    /// <item>開くものが無いとき: 指定したファイルが非対応なら M01、フォルダに対応する画像が無ければステータスバーに M14</item>
    /// <item>読めなかったものは開かない。読もうとしたのが 1 件なら M01（要るものがあれば M21 を続ける）/ M02、
    /// 2 件以上なら、読めなかったものを M29 でまとめて 1 回で知らせる</item>
    /// </list>
    /// </remarks>
    public async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (IsBusy || paths.Count == 0) return;

        var failures = new List<(string FileName, string Reason)>();
        var opened = new List<DocumentViewModel>();
        CollectResult collected;

        StatusMessage = null;
        IsBusy = true;
        try
        {
            // フォルダの中を見るので、バックグラウンドで行う（ネットワークのフォルダなどで画面を止めない）
            var openPaths = Documents.Select(d => d.Path).ToList();
            int capacity = MaxDocuments - Documents.Count;
            collected = await Task.Run(() => _collector.Collect(paths, openPaths, capacity));

            foreach (var fullPath in collected.Files)
            {
                var (document, error) = await LoadDocumentAsync(fullPath);
                if (document is not null)
                {
                    Documents.Add(document);
                    opened.Add(document);
                }
                else
                {
                    failures.Add((Path.GetFileName(fullPath), error!));
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        // 今回の中で（自然順の）最後にあって、開いている画像をアクティブにする
        var select = collected.Files.Concat(collected.AlreadyOpen)
            .Order(NaturalStringComparer.Instance)
            .Select(FindDocument)
            .LastOrDefault(d => d is not null);
        if (select is not null) ActiveDocument = select;

        var messages = new List<string>();
        if (collected.Files.Count == 0 && collected.AlreadyOpen.Count == 0 && collected.OverLimit == 0)
        {
            if (collected.Unsupported > 0) _dialogs.ShowError(Messages.M01_UnsupportedFormat);
            else if (collected.FolderHadNoImages) messages.Add(Messages.M14_FolderHasNoImages);
        }
        else if (opened.Count > 0 || collected.Unsupported > 0)
        {
            messages.Add(Messages.M09_Opened(opened.Count, collected.Unsupported));
        }
        if (collected.OverLimit > 0) messages.Add(Messages.M15_OverLimit(MaxDocuments, collected.OverLimit));
        if (messages.Count > 0) StatusMessage = string.Join(" ", messages);

        if (failures.Count > 0)
        {
            _dialogs.ShowError(collected.Files.Count == 1
                ? failures[0].Reason
                : Messages.M29_OpenFailed(failures.Select(f => (f.FileName, f.Reason.Replace("\n", " "))).ToList()));
        }
    }

    /// <summary>開いている画像を、フルパスで探す（大文字小文字は区別しない）</summary>
    private DocumentViewModel? FindDocument(string fullPath)
        => Documents.FirstOrDefault(d => string.Equals(d.Path, fullPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 画像を 1 枚読み込む。読めない形式は M01（要るものがあれば M21 を続ける）、そのほかの失敗は M02 の文を返す。
    /// 例外はアプリを落とさず、ログに書く（設計書 5.10-6）
    /// </summary>
    private async Task<(DocumentViewModel? Document, string? Error)> LoadDocumentAsync(string fullPath)
    {
        if (!_formats.CanDecode(fullPath)) return (null, Messages.M01_UnsupportedFormat);

        try
        {
            var (image, display, size) = await Task.Run(() =>
            {
                var decoded = _formats.Decode(fullPath);
                var display = DisplayImage.Create(decoded.Image);
                return (decoded.Image, display, DocumentViewModel.GetFileSize(fullPath));
            });
            return (new DocumentViewModel(fullPath, image, display, size, SelectionAspect), null);
        }
        catch (UnsupportedImageFormatException ex)
        {
            // 拡張子は対応していても、中身が読めない・OS のコーデック（拡張機能）が無い
            _log.Error($"Load not supported: {fullPath}", ex);
            return (null, ex.Requirement is { } requirement
                ? $"{Messages.M01_UnsupportedFormat}\n{Messages.M21_Requirement(requirement)}"
                : Messages.M01_UnsupportedFormat);
        }
        catch (NotSupportedException ex)
        {
            _log.Error($"Load not supported: {fullPath}", ex);
            return (null, Messages.M01_UnsupportedFormat);
        }
        catch (OutOfMemoryException ex)
        {
            _log.Error($"Load OOM: {fullPath}", ex);
            return (null, Messages.M02_LoadFailed(Messages.OutOfMemory));
        }
        catch (Exception ex)
        {
            // 壊れたファイル・大きすぎる画像・形式プラグインの例外など
            _log.Error($"Load failed: {fullPath}", ex);
            return (null, Messages.M02_LoadFailed(ex.Message));
        }
    }

    // ---------------------------------------------------------------- 切り替え

    private bool CanSwitchDocument => !IsBusy && Documents.Count > 1;

    /// <summary>Ctrl+Tab: 次の画像をアクティブにする（開いた順。最後の次は最初）</summary>
    [RelayCommand(CanExecute = nameof(CanSwitchDocument))]
    private void NextDocument() => SwitchDocument(+1);

    /// <summary>Ctrl+Shift+Tab: 前の画像をアクティブにする（最初の前は最後）</summary>
    [RelayCommand(CanExecute = nameof(CanSwitchDocument))]
    private void PreviousDocument() => SwitchDocument(-1);

    private void SwitchDocument(int step)
    {
        if (Documents.Count == 0) return;
        int index = ActiveDocument is null ? -1 : Documents.IndexOf(ActiveDocument);
        int next = index < 0 ? 0 : ((index + step) % Documents.Count + Documents.Count) % Documents.Count;
        ActiveDocument = Documents[next];
    }

    private bool CanActivateDocument(DocumentViewModel? document) => !IsBusy && document is not null && Documents.Contains(document);

    /// <summary>メニュー［ウィンドウ］の一覧で選んだ画像をアクティブにする</summary>
    [RelayCommand(CanExecute = nameof(CanActivateDocument))]
    private void ActivateDocument(DocumentViewModel? document)
    {
        if (CanActivateDocument(document)) ActiveDocument = document;
    }

    // ---------------------------------------------------------------- 並べ方（メニュー［ウィンドウ］）

    /// <summary>
    /// 子ウィンドウの並べ方を頼まれた（ワークスペースの大きさは画面にしか無いので、メインウィンドウが受けてワークスペースに並べさせる）
    /// </summary>
    public event EventHandler<WindowArrangement>? ArrangeWindowsRequested;

    private bool HasDocumentsAndIdle => !IsBusy && Documents.Count > 0;

    /// <summary>［ウィンドウ］→［重ねて表示］（左上から少しずつずらす。最大化を解く。設計書 F-02）</summary>
    [RelayCommand(CanExecute = nameof(HasDocumentsAndIdle))]
    private void CascadeWindows() => ArrangeWindowsRequested?.Invoke(this, WindowArrangement.Cascade);

    /// <summary>［ウィンドウ］→［並べて表示］（格子に分けて、すべてが見えるようにする。最大化を解く。設計書 F-02）</summary>
    [RelayCommand(CanExecute = nameof(HasDocumentsAndIdle))]
    private void TileWindows() => ArrangeWindowsRequested?.Invoke(this, WindowArrangement.Tile);

    // ---------------------------------------------------------------- 閉じる

    private bool CanClose(DocumentViewModel? document) => !IsBusy && (document ?? ActiveDocument) is not null;

    /// <summary>
    /// 画像を閉じる（設計書 F-02）。メニュー［ファイル］→［閉じる］ / Ctrl+W はアクティブな画像を（引数は null）、
    /// 子ウィンドウの［閉じる］はその画像を閉じる。ファイルは削除しない。
    /// 閉じた画像への参照はすべて外す（画像・表示用画像・履歴を手放すため。設計書 5.10-11）
    /// </summary>
    /// <remarks>書き出していない編集があるときの確認（M16）は 9-8 で足す</remarks>
    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close(DocumentViewModel? document)
    {
        document ??= ActiveDocument;
        if (document is null || IsBusy) return;

        int index = Documents.IndexOf(document);
        if (index < 0) return;

        StatusMessage = null; // 前の操作の知らせ（M09 など）を残さない
        Documents.RemoveAt(index);
        if (ActiveDocument == document)
        {
            // 次の画像（無ければ前の画像）をアクティブにする
            ActiveDocument = Documents.Count > 0 ? Documents[Math.Min(index, Documents.Count - 1)] : null;
        }
    }

    /// <summary>
    /// ［ファイル］→［すべて閉じる］（設計書 F-02）。開いている画像をすべて閉じ、参照をすべて外す。ファイルは削除しない
    /// </summary>
    /// <remarks>書き出していない編集があるときのまとめての確認（M16）は 9-8 で足す</remarks>
    [RelayCommand(CanExecute = nameof(HasDocumentsAndIdle))]
    private void CloseAll()
    {
        if (!HasDocumentsAndIdle) return;
        StatusMessage = null;
        ActiveDocument = null;
        Documents.Clear();
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
