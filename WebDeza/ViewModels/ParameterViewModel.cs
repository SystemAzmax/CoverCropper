using System.Globalization;
using System.Text;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebDeza.Imaging.Parameters;

namespace WebDeza.ViewModels;

/// <summary>
/// パラメーター 1 つ分の入力欄（設計書 5.6）。記述（ParameterDescriptor）の種類ごとの派生があり、
/// 画面は Views/Parameters/ParameterTemplates.xaml の、派生の型ごとの DataTemplate で作る。
/// フィルターのパラメーターと、保存の形式のオプションの両方に使う。
/// </summary>
/// <remarks>
/// 値は、いつも記述に合う形（ParameterDescriptor.Coerce の結果。long・double・bool・string）で持つ。
/// 入力された値は範囲に丸め、読めなければ受け付けずに今の値を出し直す。
/// </remarks>
public abstract partial class ParameterViewModel : ObservableObject
{
    private object _value;

    protected ParameterViewModel(ParameterDescriptor descriptor, object? value)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
        DefaultValue = descriptor.Coerce(descriptor.Default);
        _value = descriptor.Coerce(value);
    }

    /// <summary>記述から、その種類の入力欄を作る。value は記述に合わせる（null・読めない値は既定値）</summary>
    public static ParameterViewModel Create(ParameterDescriptor descriptor, object? value = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.Kind switch
        {
            ParameterKind.Integer or ParameterKind.Number => new NumericParameterViewModel(descriptor, value),
            ParameterKind.Boolean => new BooleanParameterViewModel(descriptor, value),
            ParameterKind.Choice => new ChoiceParameterViewModel(descriptor, value),
            ParameterKind.Color => new ColorParameterViewModel(descriptor, value),
            _ => throw new ArgumentException($"パラメーターの種類 {descriptor.Kind} の入力欄はありません。", nameof(descriptor)),
        };
    }

    /// <summary>記述の並びから、入力欄の並びを作る（値は values から。無いキーは既定値）</summary>
    public static IReadOnlyList<ParameterViewModel> CreateAll(IEnumerable<ParameterDescriptor> descriptors, ParameterValues? values = null)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        return descriptors
            .Select(d => Create(d, values is not null && values.ContainsKey(d.Key) ? values[d.Key] : null))
            .ToList();
    }

    /// <summary>入力欄の並びの今の値を、ParameterValues にまとめる</summary>
    public static ParameterValues ToValues(IEnumerable<ParameterViewModel> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters.Aggregate(ParameterValues.Empty, (values, p) => values.With(p.Key, p.Value));
    }

    /// <summary>パラメーターの記述</summary>
    public ParameterDescriptor Descriptor { get; }

    /// <summary>値を引くキー</summary>
    public string Key => Descriptor.Key;

    /// <summary>画面に出す名前</summary>
    public string DisplayName => Descriptor.DisplayName;

    /// <summary>単位（無ければ空）</summary>
    public string Unit => Descriptor.Unit ?? "";

    /// <summary>既定値（記述に合わせたもの）</summary>
    public object DefaultValue { get; }

    /// <summary>今の値（long・double・bool・string のどれか）</summary>
    public object Value => _value;

    /// <summary>今の値が既定値か</summary>
    public bool IsDefault => ParameterValues.Empty.With(Key, _value) == ParameterValues.Empty.With(Key, DefaultValue);

    /// <summary>値が変わった（フィルターのプレビューなどが受ける）</summary>
    public event EventHandler? ValueChanged;

    /// <summary>値を変える（記述に合わせる。範囲外は丸め、読めない値・null は既定値）</summary>
    public void SetValue(object? value)
    {
        var coerced = Descriptor.Coerce(value);
        if (ParameterValues.Empty.With(Key, coerced) == ParameterValues.Empty.With(Key, _value))
        {
            OnValuePresented(); // 同じ値でも、入力欄の文字は今の値で出し直す（「85.0」と打ったら「85」に）
            return;
        }
        _value = coerced;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsDefault));
        OnValuePresented();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>既定値に戻す（スライダーのダブルクリック・［リセット］）</summary>
    [RelayCommand]
    public void ResetToDefault() => SetValue(DefaultValue);

    /// <summary>値を出す派生のプロパティ（スライダーの位置・入力欄の文字など）が変わったことを知らせる</summary>
    protected abstract void OnValuePresented();

    /// <summary>入力された文字を読む（全角の数字・記号も受け付ける。前後の空白は無視する）</summary>
    protected static string NormalizeInput(string? text)
        => (text ?? "").Normalize(NormalizationForm.FormKC).Trim();
}

/// <summary>Integer・Number: スライダー（Min〜Max、Step）と数値入力。スライダーはダブルクリックで既定値に戻す</summary>
public sealed class NumericParameterViewModel : ParameterViewModel
{
    private readonly string _format;

    public NumericParameterViewModel(ParameterDescriptor descriptor, object? value) : base(descriptor, value)
    {
        if (descriptor.Kind is not (ParameterKind.Integer or ParameterKind.Number))
            throw new ArgumentException("Integer か Number の記述ではありません。", nameof(descriptor));
        _format = IsInteger ? "0" : NumberFormat(descriptor.Step);
    }

    /// <summary>整数か（Integer）</summary>
    public bool IsInteger => Descriptor.Kind == ParameterKind.Integer;

    /// <summary>スライダーの最小</summary>
    public double Minimum => IsInteger ? Math.Ceiling(Descriptor.Min) : Descriptor.Min;

    /// <summary>スライダーの最大</summary>
    public double Maximum => IsInteger ? Math.Floor(Descriptor.Max) : Descriptor.Max;

    /// <summary>スライダーの刻み（矢印キー 1 回分。記述の Step。0 以下なら 1）</summary>
    public double Step => Descriptor.Step > 0 ? Descriptor.Step : 1;

    /// <summary>スライダーの大きな刻み（PageUp / PageDown・溝のクリック）。範囲の 1/10 を刻みにそろえたもの（最低 1 刻み）</summary>
    public double LargeStep => Math.Max(Step, Math.Round((Maximum - Minimum) / 10 / Step) * Step);

    /// <summary>今の値（数値）</summary>
    public double NumberValue => Convert.ToDouble(Value, CultureInfo.InvariantCulture);

    /// <summary>スライダーの位置。動かすと値が変わる（Integer は四捨五入）</summary>
    public double SliderValue
    {
        get => NumberValue;
        set => SetValue(value);
    }

    /// <summary>数値入力の文字。Enter かフォーカスが外れたときに当てはめる。数として読めなければ、今の値に戻す</summary>
    public string Text
    {
        get => NumberValue.ToString(_format, CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(NormalizeInput(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                && double.IsFinite(number))
            {
                SetValue(number);
            }
            else
            {
                OnPropertyChanged(); // 読めない文字は受け付けず、今の値を出し直す
            }
        }
    }

    protected override void OnValuePresented()
    {
        OnPropertyChanged(nameof(NumberValue));
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(Text));
    }

    /// <summary>
    /// Number の表示の形: 刻みの小数の桁数は必ず出し（刻み 0.5 なら 8.0）、それより細かい値は 3 桁まで出す
    /// </summary>
    public static string NumberFormat(double step)
    {
        int decimals = 0;
        if (step > 0 && double.IsFinite(step))
        {
            decimal d = (decimal)step;
            while (d != decimal.Truncate(d) && decimals < 6)
            {
                d *= 10;
                decimals++;
            }
        }
        return "0." + new string('0', decimals) + "###";
    }
}

/// <summary>Boolean: チェックボックス</summary>
public sealed class BooleanParameterViewModel : ParameterViewModel
{
    public BooleanParameterViewModel(ParameterDescriptor descriptor, object? value) : base(descriptor, value)
    {
        if (descriptor.Kind != ParameterKind.Boolean)
            throw new ArgumentException("Boolean の記述ではありません。", nameof(descriptor));
    }

    /// <summary>チェックボックスの状態</summary>
    public bool IsChecked
    {
        get => (bool)Value;
        set => SetValue(value);
    }

    protected override void OnValuePresented() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>Choice: コンボボックス（選択肢の DisplayName を出し、Value を値にする）</summary>
public sealed class ChoiceParameterViewModel : ParameterViewModel
{
    public ChoiceParameterViewModel(ParameterDescriptor descriptor, object? value) : base(descriptor, value)
    {
        if (descriptor.Kind != ParameterKind.Choice)
            throw new ArgumentException("Choice の記述ではありません。", nameof(descriptor));
        Choices = descriptor.Choices ?? [];
    }

    /// <summary>選択肢（記述の順）</summary>
    public IReadOnlyList<ChoiceItem> Choices { get; }

    /// <summary>選んでいる選択肢。選択肢に無いものは受け付けない</summary>
    public ChoiceItem? SelectedChoice
    {
        get => Choices.FirstOrDefault(c => string.Equals(c.Value, (string)Value, StringComparison.Ordinal));
        set
        {
            if (value is not null && Choices.Contains(value)) SetValue(value.Value);
            else OnPropertyChanged();
        }
    }

    protected override void OnValuePresented() => OnPropertyChanged(nameof(SelectedChoice));
}

/// <summary>Color: 色見本（白・黒・灰）と #RRGGBB の入力</summary>
public sealed partial class ColorParameterViewModel : ParameterViewModel
{
    /// <summary>色見本（白・黒・灰。設計書 5.6）</summary>
    public static IReadOnlyList<string> Swatches { get; } = ["#FFFFFF", "#000000", "#808080"];

    public ColorParameterViewModel(ParameterDescriptor descriptor, object? value) : base(descriptor, value)
    {
        if (descriptor.Kind != ParameterKind.Color)
            throw new ArgumentException("Color の記述ではありません。", nameof(descriptor));
    }

    /// <summary>今の色（#RRGGBB の大文字）</summary>
    public string ColorText => (string)Value;

    /// <summary>今の色（見本の表示用）</summary>
    public Color Color => (Color)ColorConverter.ConvertFromString(ColorText);

    /// <summary>色の入力欄（#RRGGBB。「#」は省いてもよい）。Enter かフォーカスが外れたときに当てはめ、読めなければ今の値に戻す</summary>
    public string Text
    {
        get => ColorText;
        set
        {
            string text = NormalizeInput(value);
            if (!text.StartsWith('#')) text = "#" + text;
            // 読めない色は既定値にせず、今の値のままにする
            if (Descriptor.Coerce(text) is string color && string.Equals(color, text, StringComparison.OrdinalIgnoreCase))
                SetValue(color);
            else
                OnPropertyChanged();
        }
    }

    /// <summary>色見本を選ぶ</summary>
    [RelayCommand]
    private void SelectSwatch(string? color)
    {
        if (color is not null) Text = color;
    }

    protected override void OnValuePresented()
    {
        OnPropertyChanged(nameof(ColorText));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Text));
    }
}
