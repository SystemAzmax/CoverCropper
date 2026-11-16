using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebDeza.Core;
using WebDeza.Imaging;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Parameters;

namespace WebDeza.ViewModels;

/// <summary>
/// 編集パネルの「フィルター」（設計書 F-06・5.6）。フィルターの一覧と選んでいるフィルター、パラメーターの入力欄、
/// 対象の表示、プレビュー、［リセット］を持つ。対象は、アクティブな画像の選択範囲（無ければ画像全体）。
/// </summary>
/// <remarks>
/// プレビュー（設計書 5.8）: 値が既定値（IsIdentity）でない間、表示用画像（フィルター前）の対象の範囲にフィルターをかけ、
/// DocumentViewModel.SetPreview で表示する。画像も履歴も変えない。計算はバックグラウンドで 1 つずつ行い、
/// 計算中に値や選択範囲が変わったら、前の計算の始まりから PreviewInterval 以上空けて、最後の値でやり直す
/// （スライダーの操作中は間引かれ、止まったら必ず最後の値になる）。
/// プレビューをやめるのは、［リセット］・ほかのフィルターを選ぶ・ほかの画像に切り替える・その画像を編集する
/// （確定・元に戻す・やり直し）で、どれも値を既定値に戻す。
/// UI のスレッドだけから使う。
/// </remarks>
public sealed partial class FilterPanelViewModel : ObservableObject
{
    /// <summary>プレビューを更新する間隔（設計書 F-06）</summary>
    public static readonly TimeSpan DefaultPreviewInterval = TimeSpan.FromMilliseconds(50);

    private readonly AppLog _log;
    private readonly TimeSpan _previewInterval;

    private IImageFilter? _selectedFilter;
    private IReadOnlyList<ParameterViewModel> _parameters = [];
    private DocumentViewModel? _document;

    /// <summary>値をまとめて変えている間（既定値に戻すとき）は、値ごとにプレビューを頼まない</summary>
    private bool _changingValues;

    // プレビューの計算
    private volatile bool _previewRequested;
    private int _previewEpoch;
    private CancellationTokenSource _previewCancellation = new();
    private Task _previewTask = Task.CompletedTask;

    /// <param name="filters">登録されたフィルター（登録順に並べ、最初のものを選んでおく）</param>
    /// <param name="log">プレビューの計算に失敗したときに書くログ</param>
    /// <param name="previewInterval">プレビューを更新する間隔（null なら DefaultPreviewInterval）</param>
    public FilterPanelViewModel(FilterRegistry filters, AppLog log, TimeSpan? previewInterval = null)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _previewInterval = previewInterval ?? DefaultPreviewInterval;
        Filters = filters.All.ToList();
        SelectedFilter = Filters.FirstOrDefault();
    }

    /// <summary>フィルターの一覧（登録順）</summary>
    public IReadOnlyList<IImageFilter> Filters { get; }

    /// <summary>選んでいるフィルター。変えると、入力欄がそのフィルターのもの（既定値）になり、プレビューをやめる</summary>
    public IImageFilter? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedFilter) || !Filters.Contains(value)) return;
            EndPreview();
            foreach (var parameter in _parameters) parameter.ValueChanged -= OnParameterValueChanged;
            _selectedFilter = value;
            _parameters = ParameterViewModel.CreateAll(value.Parameters);
            foreach (var parameter in _parameters) parameter.ValueChanged += OnParameterValueChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Parameters));
            OnValuesChanged();
        }
    }

    /// <summary>選んでいるフィルターのパラメーターの入力欄（画面は ParameterTemplates.xaml が作る）</summary>
    public IReadOnlyList<ParameterViewModel> Parameters => _parameters;

    /// <summary>今の値</summary>
    public ParameterValues Values => ParameterViewModel.ToValues(_parameters);

    /// <summary>値がすべて既定値で、画像を変えないか（プレビューをしない）</summary>
    public bool IsIdentity => _selectedFilter is null || _selectedFilter.IsIdentity(Values.Normalize(_selectedFilter.Parameters));

    /// <summary>
    /// 対象の画像（MainViewModel が、アクティブな画像に合わせて入れる）。
    /// 変えると、前の画像のプレビューをやめ、値を既定値に戻す
    /// </summary>
    public DocumentViewModel? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(value, _document)) return;
            EndPreview();
            if (_document is not null) _document.PropertyChanged -= OnDocumentPropertyChanged;
            _document = value;
            if (_document is not null) _document.PropertyChanged += OnDocumentPropertyChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TargetText));
            ResetValues();
        }
    }

    /// <summary>対象の表示: 選択範囲があれば「対象: 選択範囲」、無ければ「対象: 画像全体」（画像が無ければ空）</summary>
    public string TargetText => _document is null ? "" : _document.Selection is null ? "対象: 画像全体" : "対象: 選択範囲";

    /// <summary>
    /// プレビューの計算（頼まれた分が終わると完了する。何もしていなければ完了済み）。テストで、プレビューが出るのを待つのに使う
    /// </summary>
    public Task PreviewTask => _previewTask;

    private bool CanReset() => _parameters.Any(p => !p.IsDefault);

    /// <summary>［リセット］: すべての値を既定値に戻す（プレビューも消える）</summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset() => ResetValues();

    /// <summary>すべての値を既定値に戻す</summary>
    private void ResetValues()
    {
        _changingValues = true;
        try
        {
            foreach (var parameter in _parameters) parameter.ResetToDefault();
        }
        finally
        {
            _changingValues = false;
        }
        OnValuesChanged();
    }

    private void OnParameterValueChanged(object? sender, EventArgs e)
    {
        if (!_changingValues) OnValuesChanged();
    }

    private void OnValuesChanged()
    {
        OnPropertyChanged(nameof(Values));
        OnPropertyChanged(nameof(IsIdentity));
        ResetCommand.NotifyCanExecuteChanged();
        RequestPreview();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DocumentViewModel.Selection):
                // 選択範囲を変えたら、新しい範囲でプレビューし直す
                OnPropertyChanged(nameof(TargetText));
                RequestPreview();
                break;
            case nameof(DocumentViewModel.Image):
                // その画像を編集した（確定・元に戻す・やり直し）ら、プレビューをやめる
                EndPreview();
                ResetValues();
                break;
        }
    }

    // ---------------------------------------------------------------- プレビュー（設計書 5.8）

    /// <summary>今の値と選択範囲でのプレビューを頼む。既定値なら、プレビューをやめる</summary>
    private void RequestPreview()
    {
        if (_document is null || _selectedFilter is null || IsIdentity)
        {
            EndPreview();
            return;
        }

        _previewRequested = true;
        if (_previewTask.IsCompleted) _previewTask = RunPreviewsAsync();
    }

    /// <summary>プレビューをやめる: 頼んである分と計算中の分を捨て、今の画像の表示に戻す</summary>
    private void EndPreview()
    {
        _previewRequested = false;
        _previewEpoch++;
        if (!_previewTask.IsCompleted)
        {
            // 計算中のものを止める（結果は、番号が変わったので捨てられる）
            _previewCancellation.Cancel();
            _previewCancellation = new CancellationTokenSource();
        }
        _document?.SetPreview(null);
    }

    /// <summary>頼まれている間、プレビューを 1 つずつ計算して表示する</summary>
    private async Task RunPreviewsAsync()
    {
        while (_previewRequested)
        {
            _previewRequested = false;
            if (_document is not { } document || _selectedFilter is not { } filter) return;

            var baseDisplay = document.BaseDisplay;
            var selection = document.Selection;
            var values = Values;
            int epoch = _previewEpoch;
            var token = _previewCancellation.Token;
            long started = Stopwatch.GetTimestamp();

            DisplayImage? preview = null;
            try
            {
                preview = await Task.Run(() => RenderPreview(baseDisplay, selection, filter, values, token));
            }
            catch (OperationCanceledException)
            {
                // プレビューをやめた
            }
            catch (Exception ex)
            {
                // フィルターの例外でアプリを落とさない。表示は変えずに、ログに書く（設計書 5.10-6）
                _log.Error($"Preview failed: {filter.Id} ({document.Path})", ex);
            }

            // 古い計算の結果は捨てる（やめた後・ほかの画像に切り替えた後・画像が変わった後のもの）
            if (preview is not null && epoch == _previewEpoch && ReferenceEquals(_document, document)
                && ReferenceEquals(document.BaseDisplay, baseDisplay))
            {
                document.SetPreview(preview);
            }

            if (_previewRequested)
            {
                var wait = _previewInterval - Stopwatch.GetElapsedTime(started);
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
            }
        }
    }

    /// <summary>
    /// 表示用画像（フィルター前）の、選択範囲に当たる範囲にフィルターをかけたものを作る（バックグラウンドで呼ぶ）。
    /// 選択範囲は表示用画像の座標に換算し、長さを持つパラメーターは PixelScale で換算させる（見た目を確定後とそろえるため）
    /// </summary>
    public static DisplayImage RenderPreview(DisplayImage baseDisplay, PixelRect? selection, IImageFilter filter,
        ParameterValues values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseDisplay);
        PixelRect? region = selection is { } rect ? baseDisplay.ToDisplayRect(rect) : null;
        var image = RegionFilter.Apply(baseDisplay.Image, region, filter, values,
            new FilterContext(baseDisplay.PixelScale, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        return baseDisplay.WithImage(image);
    }
}
