using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

public class ParameterValuesTests
{
    /// <summary>5 つの種類を 1 つずつ持つ記述（JPEG・PNG・TIFF のオプションと背景色を混ぜたもの）</summary>
    private static readonly ParameterDescriptor[] Descriptors =
    {
        new("quality", "品質", ParameterKind.Integer, 85, Min: 1, Max: 100),
        new("gamma", "ガンマ", ParameterKind.Number, 1.0, Min: 0.1, Max: 3.0, Step: 0.1),
        new("interlace", "インターレース", ParameterKind.Boolean, false),
        new("compression", "圧縮", ParameterKind.Choice, "lzw",
            Choices: new ChoiceItem[] { new("none", "なし"), new("lzw", "LZW"), new("zip", "ZIP") }),
        new("background", "背景色", ParameterKind.Color, "#FFFFFF"),
    };

    // ---------------------------------------------------------------- E12: 既定値

    [Fact]
    public void Defaults_HoldsDefaultOfEveryDescriptor()
    {
        var values = ParameterValues.Defaults(Descriptors);

        Assert.Equal(5, values.Count);
        Assert.Equal(85, values.Get<int>("quality"));
        Assert.Equal(1.0, values.Get<double>("gamma"));
        Assert.False(values.Get<bool>("interlace"));
        Assert.Equal("lzw", values.Get<string>("compression"));
        Assert.Equal("#FFFFFF", values.Get<string>("background"));
    }

    [Fact]
    public void Defaults_StoresIntegerAsLongAndNumberAsDouble()
    {
        // 既定値を int で書いても long、整数で書いた Number は double で持つ
        var descriptors = new ParameterDescriptor[]
        {
            new("quality", "品質", ParameterKind.Integer, 85, Min: 1, Max: 100),
            new("gamma", "ガンマ", ParameterKind.Number, 1, Min: 0, Max: 3),
        };

        var values = ParameterValues.Defaults(descriptors);

        Assert.IsType<long>(values["quality"]);
        Assert.IsType<double>(values["gamma"]);
    }

    // ---------------------------------------------------------------- E12: With

    [Fact]
    public void With_ReturnsChangedCopyAndLeavesOriginal()
    {
        var original = ParameterValues.Defaults(Descriptors);

        var changed = original.With("quality", 70);

        Assert.Equal(70, changed.Get<int>("quality"));
        Assert.Equal(85, original.Get<int>("quality")); // 元は変わらない
        Assert.NotSame(original, changed);
        Assert.NotEqual(original, changed);
        // ほかの値はそのまま
        Assert.Equal("lzw", changed.Get<string>("compression"));
        Assert.Equal(5, changed.Count);
    }

    [Fact]
    public void With_NewKey_AddsValue()
    {
        var values = ParameterValues.Empty.With("a", 1).With("b", true);

        Assert.Equal(2, values.Count);
        Assert.True(values.ContainsKey("a"));
        Assert.False(values.ContainsKey("c"));
        Assert.Equal(0, ParameterValues.Empty.Count); // Empty は変わらない
    }

    [Fact]
    public void With_UnsupportedValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => ParameterValues.Empty.With("a", DateTime.Now));
        Assert.Throws<ArgumentException>(() => ParameterValues.Empty.With("a", new[] { 1, 2 }));
        Assert.Throws<ArgumentException>(() => ParameterValues.Empty.With("a", double.NaN));
        Assert.Throws<ArgumentException>(() => ParameterValues.Empty.With("a", double.PositiveInfinity));
        Assert.Throws<ArgumentNullException>(() => ParameterValues.Empty.With("a", null!));
        Assert.Throws<ArgumentException>(() => ParameterValues.Empty.With("", 1));
    }

    // ---------------------------------------------------------------- E12: 中身での比較

    [Fact]
    public void Equals_SameContentInDifferentInstances_IsTrue()
    {
        // 足す順番が違っても、中身が同じなら等しい
        var a = ParameterValues.Empty.With("quality", 85).With("interlace", false).With("compression", "lzw");
        var b = ParameterValues.Empty.With("compression", "lzw").With("quality", 85).With("interlace", false);

        Assert.NotSame(a, b);
        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentContent_IsFalse()
    {
        var a = ParameterValues.Empty.With("quality", 85).With("interlace", false);

        Assert.NotEqual(a, a.With("quality", 84));                       // 値が違う
        Assert.NotEqual(a, a.With("extra", 1));                          // キーが多い
        Assert.NotEqual(a, ParameterValues.Empty.With("quality", 85));   // キーが少ない
        Assert.NotEqual(a, ParameterValues.Empty.With("Quality", 85).With("interlace", false)); // キーの大文字小文字
        Assert.NotEqual(a, a.With("interlace", "false"));                // 型が違う
        Assert.False(a.Equals(null));
        Assert.False(a == null);
        Assert.True(a != null);
    }

    [Fact]
    public void Equals_SameNumberAsIntegerAndAsReal_IsTrue()
    {
        // JSON に書いて読み直すと 1.0 が 1 になることがあっても、同じ値として扱う
        var asInteger = ParameterValues.Empty.With("gamma", 1).With("quality", 85L);
        var asReal = ParameterValues.Empty.With("gamma", 1.0).With("quality", 85.0);

        Assert.Equal(asInteger, asReal);
        Assert.Equal(asInteger.GetHashCode(), asReal.GetHashCode());
        Assert.NotEqual(asInteger, asReal.With("gamma", 1.5));
    }

    [Fact]
    public void Equals_WorksAsDictionaryValueInRecords()
    {
        // ExportSettings が中身で比べるときの土台になる
        var a = ParameterValues.Defaults(Descriptors);
        var b = ParameterValues.Defaults(Descriptors);

        Assert.True(EqualityComparer<ParameterValues>.Default.Equals(a, b));
        Assert.Single(new HashSet<ParameterValues> { a, b });
    }

    // ---------------------------------------------------------------- E12: Normalize

    [Theory]
    [InlineData(150, 100)]  // 上にはみ出す
    [InlineData(0, 1)]      // 下にはみ出す
    [InlineData(-5, 1)]
    [InlineData(70, 70)]    // 範囲内はそのまま
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    public void Normalize_IntegerOutOfRange_IsClamped(int value, int expected)
    {
        var values = ParameterValues.Defaults(Descriptors).With("quality", value).Normalize(Descriptors);

        Assert.Equal(expected, values.Get<int>("quality"));
    }

    [Theory]
    [InlineData(5.0, 3.0)]
    [InlineData(-1.0, 0.1)]
    [InlineData(2.25, 2.25)]
    public void Normalize_NumberOutOfRange_IsClamped(double value, double expected)
    {
        var values = ParameterValues.Defaults(Descriptors).With("gamma", value).Normalize(Descriptors);

        Assert.Equal(expected, values.Get<double>("gamma"));
    }

    [Fact]
    public void Normalize_MissingKeys_AreFilledWithDefaults()
    {
        var values = ParameterValues.Empty.With("quality", 70).Normalize(Descriptors);

        Assert.Equal(5, values.Count);
        Assert.Equal(70, values.Get<int>("quality")); // あった値は保つ
        Assert.Equal(1.0, values.Get<double>("gamma"));
        Assert.False(values.Get<bool>("interlace"));
        Assert.Equal("lzw", values.Get<string>("compression"));
        Assert.Equal("#FFFFFF", values.Get<string>("background"));
    }

    [Fact]
    public void Normalize_UnknownKeys_AreDropped()
    {
        var values = ParameterValues.Defaults(Descriptors).With("removedOption", 3).Normalize(Descriptors);

        Assert.False(values.ContainsKey("removedOption"));
        Assert.Equal(ParameterValues.Defaults(Descriptors), values);
    }

    [Fact]
    public void Normalize_WrongTypeOrInvalidValue_FallsBackToDefault()
    {
        var values = ParameterValues.Empty
            .With("quality", "high")        // 整数に文字列
            .With("gamma", true)            // 実数に bool
            .With("interlace", 1)           // bool に整数
            .With("compression", "jpeg")    // 選択肢に無い値
            .With("background", "white")    // 色として読めない
            .Normalize(Descriptors);

        Assert.Equal(ParameterValues.Defaults(Descriptors), values);
    }

    [Fact]
    public void Normalize_AdjustsTypeToKind()
    {
        var values = ParameterValues.Empty
            .With("quality", 84.6)          // 整数に実数 → 四捨五入
            .With("gamma", 2)               // 実数に整数 → double
            .With("compression", "zip")
            .With("background", "#1a2b3c")  // 小文字 → 大文字
            .Normalize(Descriptors);

        Assert.Equal(85L, values["quality"]);
        Assert.Equal(2.0, values["gamma"]);
        Assert.IsType<double>(values["gamma"]);
        Assert.Equal("zip", values["compression"]);
        Assert.Equal("#1A2B3C", values["background"]);
    }

    [Theory]
    [InlineData(84.5, 85)]   // 0.5 は 0 から遠い方へ
    [InlineData(84.4, 84)]
    [InlineData(99.5, 100)]
    [InlineData(100.4, 100)]
    [InlineData(0.4, 1)]     // 丸めてから範囲に収める
    public void Normalize_RealValueForInteger_IsRoundedThenClamped(double value, int expected)
    {
        var values = ParameterValues.Empty.With("quality", value).Normalize(Descriptors);

        Assert.Equal(expected, values.Get<int>("quality"));
    }

    [Theory]
    [InlineData("#FFFFFF", "#FFFFFF")]
    [InlineData("#000000", "#000000")]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("FFFFFF", "#FFFFFF")]    // # が無い → 既定値
    [InlineData("#FFF", "#FFFFFF")]      // 3 桁 → 既定値
    [InlineData("#GGGGGG", "#FFFFFF")]   // 16 進でない → 既定値
    [InlineData("#FFFFFF00", "#FFFFFF")] // 8 桁 → 既定値
    [InlineData("", "#FFFFFF")]
    public void Normalize_Color_AcceptsOnlySixHexDigits(string value, string expected)
    {
        var descriptors = new[] { new ParameterDescriptor("background", "背景色", ParameterKind.Color, "#FFFFFF") };
        // 既定値と区別できるよう、有効な色は既定値以外でも確かめる
        var values = ParameterValues.Empty.With("background", value).Normalize(descriptors);

        Assert.Equal(expected, values.Get<string>("background"));
    }

    [Fact]
    public void Normalize_IsIdempotentAndLeavesOriginal()
    {
        var original = ParameterValues.Empty.With("quality", 150).With("unknown", 1);

        var once = original.Normalize(Descriptors);
        var twice = once.Normalize(Descriptors);

        Assert.Equal(once, twice);
        Assert.Equal(150, original.Get<int>("quality")); // 元は変わらない
        Assert.True(original.ContainsKey("unknown"));
    }

    [Fact]
    public void Normalize_NoDescriptors_ReturnsEmpty()
    {
        // オプションの無い形式（BMP など）
        var values = ParameterValues.Empty.With("quality", 85).Normalize(Array.Empty<ParameterDescriptor>());

        Assert.Equal(ParameterValues.Empty, values);
        Assert.Equal(0, ParameterValues.Defaults(Array.Empty<ParameterDescriptor>()).Count);
    }

    [Fact]
    public void Normalize_InvalidDescriptors_Throw()
    {
        // 既定値が種類に合わない
        var badDefault = new[] { new ParameterDescriptor("quality", "品質", ParameterKind.Integer, "high", Min: 1, Max: 100) };
        Assert.Throws<InvalidOperationException>(() => ParameterValues.Defaults(badDefault));

        // 既定値が選択肢に無い
        var badChoice = new[]
        {
            new ParameterDescriptor("compression", "圧縮", ParameterKind.Choice, "jpeg",
                Choices: new ChoiceItem[] { new("none", "なし") }),
        };
        Assert.Throws<InvalidOperationException>(() => ParameterValues.Defaults(badChoice));

        // 範囲が逆
        var badRange = new[] { new ParameterDescriptor("quality", "品質", ParameterKind.Integer, 85, Min: 100, Max: 1) };
        Assert.Throws<InvalidOperationException>(() => ParameterValues.Defaults(badRange));

        // 同じキーが 2 つ
        var duplicated = new[]
        {
            new ParameterDescriptor("quality", "品質", ParameterKind.Integer, 85, Min: 1, Max: 100),
            new ParameterDescriptor("quality", "品質 2", ParameterKind.Integer, 50, Min: 1, Max: 100),
        };
        Assert.Throws<ArgumentException>(() => ParameterValues.Defaults(duplicated));
    }

    [Fact]
    public void Defaults_DefaultOutOfRange_IsClamped()
    {
        var descriptors = new[] { new ParameterDescriptor("quality", "品質", ParameterKind.Integer, 500, Min: 1, Max: 100) };

        Assert.Equal(100, ParameterValues.Defaults(descriptors).Get<int>("quality"));
    }

    // ---------------------------------------------------------------- Get・一覧

    [Fact]
    public void Get_ConvertsBetweenNumericTypes()
    {
        var values = ParameterValues.Empty.With("integer", 85).With("whole", 2.0).With("fraction", 2.5);

        Assert.Equal(85, values.Get<int>("integer"));
        Assert.Equal(85L, values.Get<long>("integer"));
        Assert.Equal(85.0, values.Get<double>("integer"));
        Assert.Equal(85f, values.Get<float>("integer"));

        Assert.Equal(2, values.Get<int>("whole"));       // 小数部の無い実数は整数として取り出せる
        Assert.Equal(2L, values.Get<long>("whole"));
        Assert.Equal(2.5, values.Get<double>("fraction"));
        Assert.Equal(2.5f, values.Get<float>("fraction"));
    }

    [Fact]
    public void Get_ValueNotConvertible_Throws()
    {
        var values = ParameterValues.Empty
            .With("fraction", 2.5).With("text", "lzw").With("flag", true).With("big", 5_000_000_000L);

        Assert.Throws<InvalidCastException>(() => values.Get<int>("fraction")); // 小数部がある
        Assert.Throws<InvalidCastException>(() => values.Get<int>("text"));
        Assert.Throws<InvalidCastException>(() => values.Get<bool>("text"));
        Assert.Throws<InvalidCastException>(() => values.Get<string>("flag"));
        Assert.Throws<InvalidCastException>(() => values.Get<double>("flag"));
        Assert.Throws<InvalidCastException>(() => values.Get<int>("big"));      // int に収まらない
        Assert.Equal(5_000_000_000L, values.Get<long>("big"));
    }

    [Fact]
    public void Get_MissingKey_Throws()
    {
        var values = ParameterValues.Defaults(Descriptors);

        Assert.Throws<KeyNotFoundException>(() => values.Get<int>("missing"));
        Assert.Throws<KeyNotFoundException>(() => values["missing"]);
        Assert.Throws<KeyNotFoundException>(() => values.Get<int>("Quality")); // 大文字小文字を区別する
    }

    [Fact]
    public void Keys_AreSortedAndValuesAreJsonFriendly()
    {
        var values = ParameterValues.Defaults(Descriptors);

        Assert.Equal(new[] { "background", "compression", "gamma", "interlace", "quality" }, values.Keys);
        // 値は long・double・bool・string のどれか
        Assert.All(values.Keys, key => Assert.True(values[key] is long or double or bool or string));
    }

    [Fact]
    public void ToString_ListsValuesInKeyOrder()
    {
        var values = ParameterValues.Empty.With("quality", 85).With("interlace", false)
            .With("gamma", 1.5).With("compression", "lzw");

        Assert.Equal("{compression=\"lzw\", gamma=1.5, interlace=false, quality=85}", values.ToString());
        Assert.Equal("{}", ParameterValues.Empty.ToString());
    }
}
