using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Operations;
using WebDeza.Models;
using WebDeza.Services;
using WebDeza.Views.Canvas;

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

    /// <param name="tools">登録された道具（null なら道具なし。テストで省くため）</param>
    public MainViewModel(FormatRegistry formats, SettingsService settingsService, IDialogService dialogs, AppLog log,
        ToolRegistry? tools = null)
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

        Documents.CollectionChanged += OnDocumentsChanged;

        // 選択範囲の比率は、保存してある設定から選択欄の状態を作る（設計書 F-04）
        AspectOptions = AspectOptionViewModel.CreateAll();
        _selectedAspect = AspectOptions[0];
        LoadSelectionAspect(Settings.SelectionAspect);

        // 道具のボタンは登録内容から作り、最初に登録した道具を既定にする（設計書 F-03）
        ToolItems = (tools?.All ?? []).Select(t => new ToolItemViewModel(t)).ToList();
        CurrentTool = ToolItems.FirstOrDefault()?.Factory;
    }

    /// <summary>検証済みの設定（変えたら SaveSettingsQuietly で保存する）</summary>
    public AppSettings Settings { get; }

    /// <summary>開いている画像（開いた順）</summary>
    public ObservableCollection<DocumentViewModel> Documents { get; } = new();

    /// <summary>アクティブな画像（メニュー・道具・編集パネル・ステータスバーの対象）。画像が無ければ null</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(WindowTitle), nameof(StatusText), nameof(IsEditPanelEnabled),
        nameof(UndoHeader), nameof(RedoHeader), nameof(UndoToolTip), nameof(RedoToolTip))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand), nameof(ZoomInCommand), nameof(ZoomOutCommand),
        nameof(ZoomToFitCommand), nameof(ZoomToActualSizeCommand), nameof(SelectToolCommand),
        nameof(SelectAllCommand), nameof(DeselectCommand), nameof(UseSelectToolCommand), nameof(CropCommand),
        nameof(UndoCommand), nameof(RedoCommand))]
    private DocumentViewModel? _activeDocument;

    /// <summary>読み込み中・編集の確定中（このあいだは開くなどを無効にし、カーソルを待機にする）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditPanelEnabled))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(CloseCommand), nameof(ExitCommand),
        nameof(ZoomInCommand), nameof(ZoomOutCommand), nameof(ZoomToFitCommand), nameof(ZoomToActualSizeCommand),
        nameof(SelectToolCommand), nameof(SelectAllCommand), nameof(DeselectCommand), nameof(UseSelectToolCommand),
        nameof(CropCommand), nameof(UndoCommand), nameof(RedoCommand))]
    private bool _isBusy;

    partial void OnIsBusyChanged(bool value) => NotifyDocumentCommandsChanged();

    /// <summary>読み込み中か（IsBusy の元の 1 つ。もう 1 つは、どれかの画像の編集の確定中）</summary>
    private bool _isLoading;

    /// <summary>IsBusy を、読み込み中か、どれかの画像が編集の確定中かに合わせる</summary>
    private void UpdateIsBusy() => IsBusy = _isLoading || Documents.Any(d => d.IsBusy);

    /// <summary>
    /// 画像を足した・閉じたとき: 画像ごとの編集の確定中を見張る（編集の確定は DocumentViewModel が行うので、
    /// その間アプリ全体を確定中にするため）。閉じた画像からは外す
    /// </summary>
    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DocumentViewModel document in e.OldItems) document.PropertyChanged -= OnDocumentPropertyChanged;
        if (e.NewItems is not null)
            foreach (DocumentViewModel document in e.NewItems) document.PropertyChanged += OnDocumentPropertyChanged;
        NotifyDocumentCommandsChanged();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.IsBusy)) UpdateIsBusy();
    }

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

    /// <summary>編集パネルを操作できるか（画像があり、読み込み中・編集の確定中でない。設計書 4.2）</summary>
    public bool IsEditPanelEnabled => !IsBusy && ActiveDocument is not null;

    /// <summary>編集パネルの「選択範囲」の入力欄（アクティブな画像の選択範囲）</summary>
    public SelectionPanelViewModel SelectionPanel { get; } = new();

    /// <summary>ウィンドウのタイトル: 「{アクティブな画像のファイル名} - webDeza」（画像が無いときは「webDeza」）</summary>
    public string WindowTitle => ActiveDocument is { } document
        ? $"{document.FileName} - {AppInfo.ProductName}"
        : AppInfo.ProductName;

    /// <summary>
    /// ステータスバー（設計書 4.2）: 「画像: {幅}×{高さ} ／ 選択範囲: ({X}, {Y}) {幅}×{高さ} ／ {倍率}% ／ {メッセージ}」。
    /// 選択範囲が無いときは、その項目を出さない。倍率は、子ウィンドウのキャンバスが書いた値（まだ描いていなければ出さない）
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
                if (document.ZoomPercent > 0) parts.Add(FormatZoomPercent(document.ZoomPercent));
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
        SelectionPanel.Document = newValue;
    }

    private void OnActiveDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 選択範囲・画像が変わったら、ステータスバーを更新する（子ウィンドウの位置やアクティブの印が変わっただけのときは要らない）
        if (e.PropertyName is nameof(DocumentViewModel.WindowBounds) or nameof(DocumentViewModel.IsActive)) return;
        if (e.PropertyName == nameof(DocumentViewModel.Selection))
        {
            DeselectCommand.NotifyCanExecuteChanged();
            CropCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName is nameof(DocumentViewModel.UndoName) or nameof(DocumentViewModel.RedoName))
        {
            NotifyHistoryChanged();
        }
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

    /// <summary>
    /// ドラッグ＆ドロップで受け入れるか（設計書 F-01）: 対応ファイルかフォルダを 1 つでも含む。読み込み中は受け入れない（設計書 4.2）。
    /// 受け入れたら、落とされたパスを OpenPathsAsync に渡す
    /// </summary>
    public bool CanAcceptDrop(IReadOnlyList<string>? paths)
        => !IsBusy && paths is { Count: > 0 } && _collector.ContainsOpenable(paths);

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
        _isLoading = true;
        UpdateIsBusy();
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
            _isLoading = false;
            UpdateIsBusy();
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
            return (new DocumentViewModel(fullPath, image, display, size, SelectionAspect, _dialogs, _log), null);
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

    /// <summary>ステータスバーの倍率: 10% 以上は整数、10% 未満は小数 1 桁まで（例: 37%、156%、6.3%）</summary>
    public static string FormatZoomPercent(double percent)
        => (percent >= 10 ? Math.Round(percent, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                          : percent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) + "%";

    // ---------------------------------------------------------------- ズーム（メニュー［表示］・ツールバー）

    /// <summary>
    /// ズームを頼まれた（ズームの状態は子ウィンドウのキャンバスが持つので、メインウィンドウが受けて、アクティブな画像のキャンバスに伝える。設計書 5.6）
    /// </summary>
    public event EventHandler<ZoomAction>? ZoomRequested;

    private bool CanZoom => !IsBusy && ActiveDocument is not null;

    /// <summary>［表示］→［表示拡大］ / ツールバー［＋］ / Ctrl++（中央が中心）</summary>
    [RelayCommand(CanExecute = nameof(CanZoom))]
    private void ZoomIn() => ZoomRequested?.Invoke(this, ZoomAction.In);

    /// <summary>［表示］→［表示縮小］ / ツールバー［－］ / Ctrl+-（中央が中心）</summary>
    [RelayCommand(CanExecute = nameof(CanZoom))]
    private void ZoomOut() => ZoomRequested?.Invoke(this, ZoomAction.Out);

    /// <summary>［表示］→［全体表示］ / ツールバー［全体］ / Ctrl+0</summary>
    [RelayCommand(CanExecute = nameof(CanZoom))]
    private void ZoomToFit() => ZoomRequested?.Invoke(this, ZoomAction.Fit);

    /// <summary>［表示］→［100%］ / ツールバー［100%］ / Ctrl+1</summary>
    [RelayCommand(CanExecute = nameof(CanZoom))]
    private void ZoomToActualSize() => ZoomRequested?.Invoke(this, ZoomAction.ActualSize);

    // ---------------------------------------------------------------- 道具（設計書 F-03。全画像で共通）

    /// <summary>道具のボタン（登録順）</summary>
    public IReadOnlyList<ToolItemViewModel> ToolItems { get; }

    /// <summary>今の道具（全画像で共通。子ウィンドウのキャンバスが、この種類の実体を作る）</summary>
    [ObservableProperty]
    private ICanvasToolFactory? _currentTool;

    partial void OnCurrentToolChanged(ICanvasToolFactory? value)
    {
        foreach (var item in ToolItems) item.IsSelected = ReferenceEquals(item.Factory, value);
    }

    /// <summary>画像が無いときと、読み込み中は、道具を切り替えられない（設計書 4.2）</summary>
    private bool CanSelectTool(ToolItemViewModel? item) => !IsBusy && ActiveDocument is not null && item is not null;

    /// <summary>道具のボタン</summary>
    [RelayCommand(CanExecute = nameof(CanSelectTool))]
    private void SelectTool(ToolItemViewModel? item)
    {
        if (CanSelectTool(item)) CurrentTool = item!.Factory;
    }

    /// <summary>道具のキー（M / H など。修飾キーなし）。その道具にしたら true</summary>
    public bool SelectToolByShortcut(Key key)
    {
        var item = ToolItems.FirstOrDefault(t => t.Factory.Shortcut == key);
        if (!CanSelectTool(item)) return false;
        CurrentTool = item!.Factory;
        return true;
    }

    // ---------------------------------------------------------------- 選択範囲（メニュー［選択範囲］。設計書 F-04）

    private bool CanEditSelection => !IsBusy && ActiveDocument is not null;

    /// <summary>
    /// ［選択範囲］→［すべて選択］ / Ctrl+A。比率が自由なら画像全体、比率があれば、その比率でいちばん大きい矩形を中央に置く
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void SelectAll()
    {
        if (ActiveDocument is { } document && !IsBusy)
            document.Selection = CropFrameCalculator.Initial(document.SelectionRules);
    }

    private bool CanDeselect => CanEditSelection && ActiveDocument?.Selection is not null;

    /// <summary>［選択範囲］→［選択の取り消し］ / Ctrl+D（選択範囲が無いときは無効。設計書 4.2）</summary>
    [RelayCommand(CanExecute = nameof(CanDeselect))]
    private void Deselect()
    {
        if (CanDeselect) ActiveDocument!.Selection = null;
    }

    /// <summary>［選択範囲］→［領域選択］: 「選択」の道具にする（M と同じ）</summary>
    [RelayCommand(CanExecute = nameof(CanUseSelectTool))]
    private void UseSelectTool()
    {
        if (CanUseSelectTool()) CurrentTool = SelectToolItem!.Factory;
    }

    private ToolItemViewModel? SelectToolItem => ToolItems.FirstOrDefault(t => t.Factory.Id == SelectToolFactory.ToolId);

    private bool CanUseSelectTool() => CanSelectTool(SelectToolItem);

    // ---------------------------------------------------------------- 元に戻す・やり直し（メニュー［編集］・ツールバー。設計書 F-07）

    private bool CanUndo => !IsBusy && ActiveDocument?.CanUndo == true;

    private bool CanRedo => !IsBusy && ActiveDocument?.CanRedo == true;

    /// <summary>［編集］→［元に戻す］ / ツールバー / Ctrl+Z: アクティブな画像の履歴を 1 段戻す（画像ごとの履歴）</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync()
    {
        if (!CanUndo) return Task.CompletedTask;
        StatusMessage = null;
        return ActiveDocument!.UndoAsync();
    }

    /// <summary>［編集］→［やり直し］ / ツールバー / Ctrl+Y・Ctrl+Shift+Z</summary>
    [RelayCommand(CanExecute = nameof(CanRedo))]
    private Task RedoAsync()
    {
        if (!CanRedo) return Task.CompletedTask;
        StatusMessage = null;
        return ActiveDocument!.RedoAsync();
    }

    /// <summary>メニュー［編集］の［元に戻す］の文字（例: 「元に戻す: 切り抜き(_U)」。設計書 F-07）</summary>
    public string UndoHeader => HistoryHeader("元に戻す", ActiveDocument?.UndoName, "U");

    /// <summary>メニュー［編集］の［やり直し］の文字（例: 「やり直し: 切り抜き(_R)」）</summary>
    public string RedoHeader => HistoryHeader("やり直し", ActiveDocument?.RedoName, "R");

    /// <summary>ツールバーの［元に戻す］のツールチップ（例: 「元に戻す: 切り抜き (Ctrl+Z)」）</summary>
    public string UndoToolTip => HistoryToolTip("元に戻す", ActiveDocument?.UndoName, "Ctrl+Z");

    /// <summary>ツールバーの［やり直し］のツールチップ（例: 「やり直し: 切り抜き (Ctrl+Y)」）</summary>
    public string RedoToolTip => HistoryToolTip("やり直し", ActiveDocument?.RedoName, "Ctrl+Y");

    /// <summary>
    /// 元に戻す・やり直しのメニューの文字。操作の名前があれば「{動作}: {名前}(_{キー})」、無ければ「{動作}(_{キー})」。
    /// 名前の「_」は、アクセスキーの印と見なされないように「__」にする
    /// </summary>
    public static string HistoryHeader(string action, string? operationName, string accessKey)
        => operationName is null
            ? $"{action}(_{accessKey})"
            : $"{action}: {operationName.Replace("_", "__")}(_{accessKey})";

    private static string HistoryToolTip(string action, string? operationName, string gesture)
        => operationName is null ? $"{action} ({gesture})" : $"{action}: {operationName} ({gesture})";

    /// <summary>アクティブな画像の履歴が変わったことを、コマンドとメニューの文字に知らせる</summary>
    private void NotifyHistoryChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(UndoHeader));
        OnPropertyChanged(nameof(RedoHeader));
        OnPropertyChanged(nameof(UndoToolTip));
        OnPropertyChanged(nameof(RedoToolTip));
    }

    // ---------------------------------------------------------------- 画像の編集（メニュー［画像］。設計書 F-04・F-05）

    /// <summary>切り抜きの、元に戻す・やり直しのメニューに出す名前</summary>
    public const string CropEditName = "切り抜き";

    private bool CanCrop => !IsBusy && ActiveDocument?.Selection is not null;

    /// <summary>
    /// ［画像］→［選択範囲で切り抜き］ / Ctrl+Shift+X / 編集パネルの［切り抜き］（設計書 F-04）。
    /// 画像を選択範囲の大きさに切り出し、選択範囲を解除して全体表示に戻す。履歴に 1 段積む。選択範囲が無いときは無効
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCrop))]
    private Task CropAsync()
    {
        if (!CanCrop) return Task.CompletedTask;
        var selection = ActiveDocument!.Selection!.Value;
        return ApplyEditAsync(ActiveDocument, CropEditName, (image, token) => Crop.Apply(image, selection, token),
            changedRegion: null, newSelection: null, resetView: true);
    }

    /// <summary>
    /// アクティブな画像に編集を確定させる（メニューのコマンドの共通の入り口）。前の操作の知らせ（M09 など）は消す。
    /// 確定中は、DocumentViewModel.IsBusy を見て、アプリ全体を確定中にする
    /// </summary>
    private Task ApplyEditAsync(DocumentViewModel document, string name, Func<ImageBuffer, CancellationToken, ImageBuffer> edit,
        PixelRect? changedRegion, PixelRect? newSelection, bool resetView)
    {
        if (IsBusy) return Task.CompletedTask;
        StatusMessage = null;
        return document.ApplyEditAsync(name, edit, changedRegion, newSelection, resetView);
    }

    // ---------------------------------------------------------------- 選択範囲の比率（編集パネルの「選択範囲」。設計書 F-04）

    private AspectOptionViewModel _selectedAspect;

    /// <summary>決まった比率を縦長（3:4 など）にしているか（［縦横を入れ替え］。決まった比率を選び直しても保つ）</summary>
    private bool _aspectPortrait;

    /// <summary>カスタムの比率（幅:高さ。1〜SelectionAspectData.MaxRatioLength）</summary>
    private int _customAspectWidth = 1, _customAspectHeight = 1;

    /// <summary>比率の選択欄の項目（自由 / 元画像と同じ / 1:1 / 4:3 / 3:2 / 16:9 / カスタム）</summary>
    public IReadOnlyList<AspectOptionViewModel> AspectOptions { get; }

    /// <summary>
    /// 選んでいる比率。変えたら、設定に保存し、開いているすべての画像の選択範囲を新しい比率に合わせ直す
    /// （比率は全画像で共通。中心と面積をなるべく保つ）。決まった比率からカスタムにしたときは、その比率から始める
    /// </summary>
    public AspectOptionViewModel SelectedAspect
    {
        get => _selectedAspect;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedAspect) || !AspectOptions.Contains(value)) return;
            if (value.Choice == AspectChoice.Custom && _selectedAspect.Choice == AspectChoice.Preset)
            {
                (_customAspectWidth, _customAspectHeight) = _selectedAspect.Oriented(_aspectPortrait);
                OnPropertyChanged(nameof(CustomAspectWidthText));
                OnPropertyChanged(nameof(CustomAspectHeightText));
            }
            _selectedAspect = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomAspect));
            ApplySelectionAspect();
        }
    }

    /// <summary>カスタムの比率を選んでいるか（幅:高さの入力欄を出す）</summary>
    public bool IsCustomAspect => _selectedAspect.Choice == AspectChoice.Custom;

    /// <summary>カスタムの比率の幅（Enter かフォーカスが外れたときに当てはめる。1〜10000 に収め、整数でなければ元に戻す）</summary>
    public string CustomAspectWidthText
    {
        get => _customAspectWidth.ToString(CultureInfo.InvariantCulture);
        set => SetCustomAspect(value, isWidth: true);
    }

    /// <summary>カスタムの比率の高さ</summary>
    public string CustomAspectHeightText
    {
        get => _customAspectHeight.ToString(CultureInfo.InvariantCulture);
        set => SetCustomAspect(value, isWidth: false);
    }

    /// <summary>今の選択を、設定に保存する形にしたもの</summary>
    public SelectionAspectData CurrentSelectionAspect
    {
        get
        {
            switch (_selectedAspect.Choice)
            {
                case AspectChoice.Original:
                    return SelectionAspectData.Original();
                case AspectChoice.Preset:
                    var (width, height) = _selectedAspect.Oriented(_aspectPortrait);
                    return SelectionAspectData.Fixed(width, height);
                case AspectChoice.Custom:
                    return SelectionAspectData.Fixed(_customAspectWidth, _customAspectHeight);
                default:
                    return SelectionAspectData.Free();
            }
        }
    }

    /// <summary>［縦横を入れ替え］できるか: 幅と高さの違う比率（決まった比率かカスタム）を選んでいる</summary>
    private bool CanSwapAspect() => CurrentSelectionAspect is { Kind: SelectionAspectData.FixedKind } a && a.Width != a.Height;

    /// <summary>［縦横を入れ替え］: 4:3 ↔ 3:4 など。選択範囲も、入れ替えた比率に合わせ直す</summary>
    [RelayCommand(CanExecute = nameof(CanSwapAspect))]
    private void SwapAspect()
    {
        if (!CanSwapAspect()) return;
        if (_selectedAspect.Choice == AspectChoice.Custom)
        {
            (_customAspectWidth, _customAspectHeight) = (_customAspectHeight, _customAspectWidth);
            OnPropertyChanged(nameof(CustomAspectWidthText));
            OnPropertyChanged(nameof(CustomAspectHeightText));
        }
        else
        {
            _aspectPortrait = !_aspectPortrait;
            foreach (var option in AspectOptions) option.UpdateLabel(_aspectPortrait);
        }
        ApplySelectionAspect();
    }

    /// <summary>
    /// 保存してある比率から、選択欄の状態を作る。決まった比率（縦長も）は、その項目にする。
    /// それ以外の fixed はカスタム（カスタムで 4:3 を入れたものは、次に起動したときは 4:3 の項目になる）
    /// </summary>
    private void LoadSelectionAspect(SelectionAspectData data)
    {
        var option = AspectOptions[0];
        if (data.Kind == SelectionAspectData.OriginalKind)
        {
            option = AspectOptions.First(o => o.Choice == AspectChoice.Original);
        }
        else if (data is { Kind: SelectionAspectData.FixedKind, IsValid: true })
        {
            var preset = AspectOptions.FirstOrDefault(o => o.Choice == AspectChoice.Preset
                && ((o.Width, o.Height) == (data.Width, data.Height) || (o.Height, o.Width) == (data.Width, data.Height)));
            if (preset is not null)
            {
                option = preset;
                _aspectPortrait = data.Width < data.Height;
            }
            else
            {
                option = AspectOptions.First(o => o.Choice == AspectChoice.Custom);
                (_customAspectWidth, _customAspectHeight) = (data.Width, data.Height);
            }
        }
        _selectedAspect = option;
        foreach (var o in AspectOptions) o.UpdateLabel(_aspectPortrait);
    }

    private void SetCustomAspect(string? text, bool isWidth)
    {
        if (SelectionPanelViewModel.TryParseInput(text, out int value))
        {
            value = Math.Clamp(value, 1, SelectionAspectData.MaxRatioLength);
            int old = isWidth ? _customAspectWidth : _customAspectHeight;
            if (isWidth) _customAspectWidth = value;
            else _customAspectHeight = value;
            if (value != old && IsCustomAspect) ApplySelectionAspect();
        }

        // 整数でない・収めた値になったときも、今の値を出し直す
        OnPropertyChanged(isWidth ? nameof(CustomAspectWidthText) : nameof(CustomAspectHeightText));
    }

    /// <summary>今の比率を設定に保存し、開いているすべての画像の選択範囲を合わせ直す</summary>
    private void ApplySelectionAspect()
    {
        Settings.SelectionAspect = CurrentSelectionAspect;
        SaveSettingsQuietly();
        foreach (var document in Documents) document.RefitSelection();
        SwapAspectCommand.NotifyCanExecuteChanged();
    }

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
        foreach (var document in Documents) document.PropertyChanged -= OnDocumentPropertyChanged; // Clear は外した画像を知らせない
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
