using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;

namespace WebDeza.ViewModels;

/// <summary>選択範囲の入力欄（X・Y・幅・高さ）</summary>
public enum SelectionField
{
    X,
    Y,
    Width,
    Height,
}

/// <summary>
/// 編集パネルの「選択範囲」の入力欄（設計書 F-04・4.1）。アクティブな画像の選択範囲の X・Y・幅・高さを出し、
/// 入力された値で選択範囲を変える（画像の内側に収める）。選択範囲が無いときは空欄で、入力できない。
/// ［すべて選択］［解除］は MainViewModel のコマンドを使う。
/// </summary>
public sealed partial class SelectionPanelViewModel : ObservableObject
{
    private DocumentViewModel? _document;

    /// <summary>対象の画像（MainViewModel が、アクティブな画像に合わせて入れる）</summary>
    public DocumentViewModel? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(_document, value)) return;
            if (_document is not null) _document.PropertyChanged -= OnDocumentPropertyChanged;
            _document = value;
            if (_document is not null) _document.PropertyChanged += OnDocumentPropertyChanged;
            OnPropertyChanged();
            RefreshValues();
        }
    }

    /// <summary>選択範囲があるか（入力欄を有効にする）</summary>
    public bool HasSelection => _document?.Selection is not null;

    /// <summary>X（左端。px）</summary>
    public string XText
    {
        get => Format(SelectionField.X);
        set => Apply(SelectionField.X, value);
    }

    /// <summary>Y（上端。px）</summary>
    public string YText
    {
        get => Format(SelectionField.Y);
        set => Apply(SelectionField.Y, value);
    }

    /// <summary>幅（px）</summary>
    public string WidthText
    {
        get => Format(SelectionField.Width);
        set => Apply(SelectionField.Width, value);
    }

    /// <summary>高さ（px）</summary>
    public string HeightText
    {
        get => Format(SelectionField.Height);
        set => Apply(SelectionField.Height, value);
    }

    // ---------------------------------------------------------------- 計算（画面を使わずにテストできる）

    /// <summary>
    /// 入力欄 1 つの値を、選択範囲に当てはめる。ほかの値は変えずに、規則（大きさの最小〜最大と比率）と画像の内側に収める。
    /// 位置は、大きさを保てるなら保ち、はみ出す分は内側へずらす（CropFrameCalculator.Clamp）。
    /// 比率が決まっているときは、幅を入れたら高さを、高さを入れたら幅を、比率から求める
    /// </summary>
    /// <param name="current">今の選択範囲</param>
    /// <param name="field">入力した欄</param>
    /// <param name="value">入力した値</param>
    /// <param name="rules">選択範囲の規則</param>
    public static PixelRect ApplyField(PixelRect current, SelectionField field, int value, FrameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var rect = field switch
        {
            SelectionField.X => current with { X = value },
            SelectionField.Y => current with { Y = value },
            SelectionField.Width => current with { Width = value },
            SelectionField.Height when rules.Aspect is { } a => current with { Width = WidthForHeight(value, a) },
            SelectionField.Height => current with { Height = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };
        return CropFrameCalculator.Clamp(rect, rules);
    }

    /// <summary>
    /// 入力欄の文字を整数にする（前後の空白と、全角の数字・符号も受け付ける）。整数でなければ false
    /// </summary>
    public static bool TryParseInput(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = text.Normalize(NormalizationForm.FormKC).Trim();
        return int.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    // ---------------------------------------------------------------- 内部

    private string Format(SelectionField field)
    {
        if (_document?.Selection is not { } s) return "";
        int value = field switch
        {
            SelectionField.X => s.X,
            SelectionField.Y => s.Y,
            SelectionField.Width => s.Width,
            _ => s.Height,
        };
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private void Apply(SelectionField field, string? text)
    {
        if (_document is { Selection: { } current } document && TryParseInput(text, out int value))
            document.Selection = ApplyField(current, field, value, document.SelectionRules);

        // 整数でない・収めた値になった・変わらなかったときも、今の値を出し直す
        RefreshValues();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentViewModel.Selection) or null or "") RefreshValues();
    }

    private void RefreshValues()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(XText));
        OnPropertyChanged(nameof(YText));
        OnPropertyChanged(nameof(WidthText));
        OnPropertyChanged(nameof(HeightText));
    }

    /// <summary>高さから、比率どおりの幅（四捨五入。1 以上）</summary>
    private static int WidthForHeight(int height, AspectRatio aspect)
    {
        long h = Math.Max(1, height);
        long width = (2 * h * aspect.Width + aspect.Height) / (2L * aspect.Height);
        return (int)Math.Clamp(width, 1, int.MaxValue);
    }
}
