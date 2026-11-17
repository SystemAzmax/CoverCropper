using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Models;
using WebDeza.Services;

namespace WebDeza.ViewModels;

/// <summary>
/// 保存のダイアログ（設計書 F-08・F-10・4.6）。アクティブな画像の今の画像（編集を確定した結果の全体）を、
/// 保存の設定で出力先に保存する。保存の処理はダイアログの中で行い、失敗したときはダイアログを閉じない。
/// </summary>
/// <remarks>
/// 出力先は［変更…］で選び直せ、［開く］でエクスプローラーに出す。変えた出力先と設定は、保存できたときだけ
/// MainViewModel が設定に記録する（Saved を受けて）。［キャンセル］では何も記録しない（設計書 F-08）。
/// 形式は保存できる形式の一覧から選び、オプションの入力欄は形式の記述から作る（ParameterViewModel）。
/// 形式を切り替えても、それぞれの形式のオプションの値は覚えておく。
/// 背景色は色見本と #RRGGBB で選び、透過を保てない形式を選んでいるときは M24 を出す。
/// 品質の決め方（上限）は、渡された保存の設定のまま使う（入力欄は 10-7 で足す）。
/// 保存サイズ（設計書 F-09）は、開いたときと設定（形式・オプション・背景色）を変えたときに ExportPreviewService で計算する。
/// 頼む間隔は、前に頼んでから SizeInterval 以上空ける（スライダーの操作中は間引かれ、止まったら必ず最後の値で計算する）。
/// 保存するときは、同じ条件の計算結果があればそのバイト列を書く（表示したサイズと保存したファイルのサイズが一致する）。
/// 計算中でも［保存］は押せる（そのときは保存の処理でエンコードし直す）。
/// </remarks>
public sealed partial class ExportDialogViewModel : ObservableObject
{
    private readonly DocumentViewModel _document;
    private readonly ExportService _exporter;
    private readonly IDialogService _dialogs;
    private readonly AppLog _log;
    private readonly ExportPreviewService _preview;
    private readonly TimeSpan _sizeInterval;

    /// <summary>保存サイズの計算を頼む間隔（設計書 F-09）</summary>
    public static readonly TimeSpan DefaultSizeInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>形式の Id → オプション（形式を切り替えたときに、前の形式の値をここに覚える）</summary>
    private readonly Dictionary<string, ParameterValues> _formatOptions;
    private readonly string _initialFormatId;
    private readonly long? _sizeLimitBytes;

    /// <summary>背景色の入力欄の記述（既定は白。設計書 F-08）</summary>
    public static readonly ParameterDescriptor BackgroundDescriptor =
        new("background", "背景色", ParameterKind.Color, SettingsService.DefaultBackground);

    private IImageFormat? _selectedFormat;
    private IReadOnlyList<ParameterViewModel> _options = [];

    /// <param name="document">保存する画像</param>
    /// <param name="outputFolder">出力先（設定のもの。未設定なら null）</param>
    /// <param name="settings">保存の設定（前回［保存］を押したときのもの）</param>
    /// <param name="exporter">保存の処理</param>
    /// <param name="dialogs">メッセージとフォルダの選択</param>
    /// <param name="log">保存・保存サイズの計算に失敗したときに書くログ</param>
    /// <param name="sizeInterval">保存サイズの計算を頼む間隔（null なら DefaultSizeInterval。テストで縮める）</param>
    public ExportDialogViewModel(DocumentViewModel document, string? outputFolder, ExportSettings settings,
        ExportService exporter, IDialogService dialogs, AppLog log, TimeSpan? sizeInterval = null)
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
        _sizeInterval = sizeInterval ?? DefaultSizeInterval;
        _preview = exporter.CreatePreviewService();
        _preview.Completed += OnPreviewCompleted;
        _preview.Failed += OnPreviewFailed;

        _formatOptions = new Dictionary<string, ParameterValues>(settings.FormatOptions, StringComparer.Ordinal);
        _initialFormatId = settings.FormatId;
        _sizeLimitBytes = settings.SizeLimitBytes;
        Background = new ColorParameterViewModel(BackgroundDescriptor, ColorText.Format(settings.Background));
        Background.ValueChanged += (_, _) => OnSettingsChanged();
        Formats = exporter.EncodableFormats;
        // 設定の形式が無ければ（登録が変わったなど）、一覧の最初の形式にする
        SelectFormat(Formats.FirstOrDefault(f => string.Equals(f.Id, settings.FormatId, StringComparison.Ordinal))
            ?? Formats.FirstOrDefault());
    }

    /// <summary>保存する画像</summary>
    public DocumentViewModel Document => _document;

    /// <summary>ダイアログのタイトル: 「保存: {ファイル名}」</summary>
    public string Title => $"保存: {_document.FileName}";

    /// <summary>
    /// 今の入力から作った保存の設定（保存できたら、MainViewModel が設定に記録する）。
    /// FormatOptions には、今の形式の値と、切り替える前に覚えたほかの形式の値が入る
    /// </summary>
    public ExportSettings Settings
    {
        get
        {
            var options = new Dictionary<string, ParameterValues>(_formatOptions, StringComparer.Ordinal);
            if (_selectedFormat is { } format) options[format.Id] = ParameterViewModel.ToValues(_options);
            return new ExportSettings(_selectedFormat?.Id ?? _initialFormatId, options, _sizeLimitBytes,
                ColorText.Parse(Background.ColorText));
        }
    }

    // ---------------------------------------------------------------- 形式とオプション（設計書 F-08）

    /// <summary>保存できる形式（登録順。標準は JPEG / PNG / BMP / TIFF）</summary>
    public IReadOnlyList<IImageFormat> Formats { get; }

    /// <summary>
    /// 選んでいる形式。変えると、今の形式のオプションの値を覚えてから、その形式の入力欄を（覚えている値で）作る。
    /// ファイル名の拡張子も変わる
    /// </summary>
    public IImageFormat? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedFormat) || !Formats.Contains(value)) return;
            SelectFormat(value);
        }
    }

    // ---------------------------------------------------------------- 背景色（設計書 F-08）

    /// <summary>
    /// 背景色の入力欄（色見本（白・黒・灰）と #RRGGBB）。透過を保てない形式で保存するとき、透過部分をこの色で塗る。
    /// 変えると Settings の変更を知らせる（保存する画像が作り直される）
    /// </summary>
    public ColorParameterViewModel Background { get; }

    /// <summary>M24（「{形式} は透過を保てないため、透過部分を背景色で塗ります。」）。透過を保てる形式なら null</summary>
    public string? AlphaNotice
        => _selectedFormat is { } format && !format.Capabilities.HasFlag(FormatCapabilities.Alpha)
            ? Messages.M24_AlphaFlattened(format.DisplayName)
            : null;

    /// <summary>M24 を出すか（透過を保てない形式を選んでいる）</summary>
    public bool HasAlphaNotice => AlphaNotice is not null;

    /// <summary>選んでいる形式のオプションの入力欄（画面は ParameterTemplates.xaml が作る）</summary>
    public IReadOnlyList<ParameterViewModel> Options => _options;

    /// <summary>選んでいる形式にオプションが無いか（BMP など。「オプションはありません」を出す）</summary>
    public bool HasNoOptions => _selectedFormat is not null && _options.Count == 0;

    private void SelectFormat(IImageFormat? format)
    {
        if (_selectedFormat is { } previous) _formatOptions[previous.Id] = ParameterViewModel.ToValues(_options);
        foreach (var option in _options) option.ValueChanged -= OnOptionValueChanged;

        _selectedFormat = format;
        _options = format?.Encoder is { } encoder
            ? ParameterViewModel.CreateAll(encoder.Options, _formatOptions.GetValueOrDefault(format.Id))
            : [];
        foreach (var option in _options) option.ValueChanged += OnOptionValueChanged;

        OnPropertyChanged(nameof(SelectedFormat));
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(HasNoOptions));
        OnPropertyChanged(nameof(AlphaNotice));
        OnPropertyChanged(nameof(HasAlphaNotice));
        SaveCommand.NotifyCanExecuteChanged();
        RefreshFileName();
        OnSettingsChanged();
    }

    private void OnOptionValueChanged(object? sender, EventArgs e) => OnSettingsChanged();

    /// <summary>保存の設定が変わった: 変更を知らせ、保存サイズを計算し直す</summary>
    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(Settings));
        RequestSize();
    }

    // ---------------------------------------------------------------- 保存サイズ（設計書 F-09）

    private bool _closed;
    private bool _sizeScheduled;
    private long _lastSizeRequest;
    private Task _sizeTask = Task.CompletedTask;

    /// <summary>保存サイズの表示（例: 「182.4 KB（元ファイルの 6%）」。計算中は「計算中…」、失敗したら「—」）</summary>
    [ObservableProperty]
    private string _fileSizeText = CalculatingText;

    /// <summary>保存サイズのツールチップ（バイト数。例: 「186,778 バイト」）。計算中・失敗したときは null</summary>
    [ObservableProperty]
    private string? _fileSizeToolTip;

    /// <summary>保存サイズを計算しているか（計算中、または頼む前の待ち）</summary>
    [ObservableProperty]
    private bool _isCalculatingSize = true;

    /// <summary>計算中の表示</summary>
    public const string CalculatingText = "計算中…";

    /// <summary>計算できなかったときの表示</summary>
    public const string UnknownSizeText = "—";

    /// <summary>今の設定での保存サイズの計算結果（まだ無い・計算中・失敗なら null）</summary>
    public ExportPreviewResult? SizeResult { get; private set; }

    /// <summary>
    /// 頼んである保存サイズの計算が、すべて終わるのを待つ（テストで、表示が出るのを待つのに使う）
    /// </summary>
    public async Task WaitForSizeAsync()
    {
        while (true)
        {
            var task = _sizeTask;
            await task;
            if (ReferenceEquals(task, _sizeTask) && !_sizeScheduled) return;
        }
    }

    /// <summary>
    /// 保存サイズの計算を頼む。前に頼んでから SizeInterval 以上空いていればすぐ、空いていなければその時まで待ってから、
    /// そのときの最後の設定で頼む（待っている間の変更はまとめる）
    /// </summary>
    private void RequestSize()
    {
        if (_closed) return;
        SizeResult = null;
        IsCalculatingSize = true;
        FileSizeText = CalculatingText;
        FileSizeToolTip = null;
        if (_sizeScheduled) return;

        var wait = _sizeInterval - Stopwatch.GetElapsedTime(_lastSizeRequest);
        if (wait <= TimeSpan.Zero)
        {
            _sizeTask = StartSizeRequestAsync();
        }
        else
        {
            _sizeScheduled = true;
            _sizeTask = RequestSizeLaterAsync(wait);
        }
    }

    private async Task RequestSizeLaterAsync(TimeSpan wait)
    {
        await Task.Delay(wait);
        _sizeScheduled = false;
        if (_closed) return;
        await StartSizeRequestAsync();
    }

    private Task StartSizeRequestAsync()
    {
        _lastSizeRequest = Stopwatch.GetTimestamp();
        return _selectedFormat is null
            ? Task.CompletedTask
            : _preview.RequestAsync(_document.Image, Settings); // 結果は Completed / Failed で受ける
    }

    private void OnPreviewCompleted(object? sender, ExportPreviewResult result)
    {
        // 頼んだ後に設定が変わっていれば（次を頼む前の待ちの間など）、その結果は出さない
        if (_closed || !result.Matches(_document.Image, Settings)) return;
        SizeResult = result;
        IsCalculatingSize = false;
        FileSizeText = FormatFileSize(result.FileSize, _document.SourceFileSize);
        FileSizeToolTip = string.Format(CultureInfo.InvariantCulture, "{0:N0} バイト", result.FileSize);
    }

    private void OnPreviewFailed(object? sender, Exception ex)
    {
        // 形式プラグインの例外などでも、ダイアログは止めない。表示を「—」にしてログに書く（設計書 5.10-6）
        _log.Error($"Export size failed: {_document.Path} ({Settings.FormatId})", ex);
        if (_closed) return;
        SizeResult = null;
        IsCalculatingSize = false;
        FileSizeText = UnknownSizeText;
        FileSizeToolTip = null;
    }

    /// <summary>
    /// 保存サイズの表示: 「182.4 KB（元ファイルの 6%）」（設計書 F-09）。元ファイルのバイト数が分からなければ「182.4 KB」だけ。
    /// 割合は、1% 以上は整数（四捨五入）、1% 未満は小数 1 桁まで（0% と出さないように）
    /// </summary>
    public static string FormatFileSize(long bytes, long sourceFileSize)
    {
        var size = FileSizeFormatter.Format(bytes);
        if (sourceFileSize <= 0) return size;

        double percent = bytes * 100.0 / sourceFileSize;
        string ratio = percent >= 1
            ? Math.Round(percent, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            : percent.ToString("0.#", CultureInfo.InvariantCulture);
        return $"{size}（元ファイルの {ratio}%）";
    }

    /// <summary>ダイアログを閉じた: 保存サイズの計算をやめる</summary>
    public void OnClosed()
    {
        _closed = true;
        _preview.Cancel();
    }

    /// <summary>保存する大きさ（今の画像の大きさのまま保存する。設計書 F-08）</summary>
    public string SizeText => $"{_document.ImageWidth} × {_document.ImageHeight} px";

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
            FileName = _selectedFormat is { } format
                ? _exporter.CreateOutputFileName(OutputFolder, _document.Path, format.Id)
                : "";
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

    private bool CanSave() => !IsSaving && _selectedFormat is not null;

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
            // 同じ条件の保存サイズを計算してあれば、そのバイト列をそのまま書く（表示したサイズと一致させる）
            var computed = _preview.Latest;
            result = await Task.Run(() => _exporter.Export(image, sourcePath, folder, settings, precomputed: computed));
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
