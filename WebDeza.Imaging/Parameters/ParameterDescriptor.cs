namespace WebDeza.Imaging.Parameters;

/// <summary>パラメーターの種類。値の型と、画面の入力欄の種類を決める</summary>
public enum ParameterKind
{
    /// <summary>整数（値は long）。Min〜Max の範囲を持つ</summary>
    Integer,

    /// <summary>実数（値は double）。Min〜Max の範囲を持つ</summary>
    Number,

    /// <summary>真偽（値は bool）</summary>
    Boolean,

    /// <summary>選択肢から 1 つ（値は選択肢の Value の string）</summary>
    Choice,

    /// <summary>色（値は "#RRGGBB" の string）</summary>
    Color,
}

/// <summary>Choice の選択肢。Value は保存される値、DisplayName は画面に出す名前</summary>
public sealed record ChoiceItem(string Value, string DisplayName);

/// <summary>
/// パラメーター 1 つの記述。フィルターのパラメーターと、出力形式のオプションで共用する。
/// 画面の入力欄は、この記述から自動で作る。
/// </summary>
/// <param name="Key">値を引くキー。設定・プリセット・レシピに書くので変えない</param>
/// <param name="DisplayName">画面に出す名前</param>
/// <param name="Kind">種類</param>
/// <param name="Default">既定値。種類に合う型（Integer は整数、Number は数値、Boolean は bool、Choice と Color は string）で渡す</param>
/// <param name="Min">最小（Integer・Number のとき）</param>
/// <param name="Max">最大（Integer・Number のとき）</param>
/// <param name="Step">スライダーの刻み（Integer・Number のとき。値の丸めには使わない）</param>
/// <param name="Unit">単位の表示（"%"・"KB" など）</param>
/// <param name="Choices">選択肢（Choice のとき）</param>
public sealed record ParameterDescriptor(
    string Key, string DisplayName, ParameterKind Kind, object Default,
    double Min = 0, double Max = 0, double Step = 1, string? Unit = null,
    IReadOnlyList<ChoiceItem>? Choices = null)
{
    /// <summary>
    /// 値をこの記述に合う形にして返す。
    /// 型を種類に合わせ（Integer は四捨五入して long、Number は double）、範囲外は Min〜Max に丸める。
    /// 種類に合わない値・選択肢に無い値・色として読めない値・null は、既定値にする。
    /// </summary>
    /// <exception cref="InvalidOperationException">記述そのものが正しくない（既定値が種類に合わない、Min が Max より大きい）</exception>
    public object Coerce(object? value)
    {
        if (value is not null && TryCoerce(value, out object coerced)) return coerced;
        if (Default is not null && TryCoerce(Default, out object fallback)) return fallback;

        throw new InvalidOperationException(
            $"パラメーター「{Key}」の既定値（{Default ?? "null"}）が、種類 {Kind} に合っていません。");
    }

    private bool TryCoerce(object value, out object result)
    {
        if (!ParameterValues.TryCanonicalize(value, out object canonical))
        {
            result = value;
            return false;
        }
        result = canonical;

        switch (Kind)
        {
            case ParameterKind.Integer:
            {
                if (!TryGetNumber(canonical, out double number)) return false;
                // 範囲に小数が指定されていても、整数として取れる範囲に収める
                double min = Math.Ceiling(Min), max = Math.Floor(Max);
                EnsureRange(min, max);
                result = (long)Math.Clamp(Math.Round(number, MidpointRounding.AwayFromZero), min, max);
                return true;
            }

            case ParameterKind.Number:
            {
                if (!TryGetNumber(canonical, out double number)) return false;
                EnsureRange(Min, Max);
                result = Math.Clamp(number, Min, Max);
                return true;
            }

            case ParameterKind.Boolean:
                return canonical is bool;

            case ParameterKind.Choice:
                return canonical is string choice
                    && Choices is not null
                    && Choices.Any(c => string.Equals(c.Value, choice, StringComparison.Ordinal));

            case ParameterKind.Color:
                if (canonical is not string color || !IsHexColor(color)) return false;
                result = color.ToUpperInvariant();
                return true;

            default:
                return false;
        }
    }

    private void EnsureRange(double min, double max)
    {
        if (!(min <= max))
            throw new InvalidOperationException(
                $"パラメーター「{Key}」の範囲（{Min}〜{Max}）が正しくありません。");
    }

    private static bool TryGetNumber(object canonical, out double number)
    {
        switch (canonical)
        {
            case long l:
                number = l;
                return true;
            case double d:
                number = d;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    /// <summary>"#RRGGBB"（16 進 6 桁。大文字小文字は問わない）か</summary>
    private static bool IsHexColor(string text)
    {
        if (text.Length != 7 || text[0] != '#') return false;
        for (int i = 1; i < text.Length; i++)
        {
            if (!char.IsAsciiHexDigit(text[i])) return false;
        }
        return true;
    }
}
