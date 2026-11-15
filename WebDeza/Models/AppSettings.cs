using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Models;

/// <summary>
/// %APPDATA%\webDeza\settings.json に保存する設定（設計書 F-12）。
/// JSON にそのまま書く形なので、値は検証していない。読み込んだ値は SettingsService が検証してから使う。
/// </summary>
public sealed class AppSettings
{
    /// <summary>書き出し先のフォルダ</summary>
    public string? OutputFolder { get; set; }

    /// <summary>前回「開く」で使ったフォルダ</summary>
    public string? LastOpenFolder { get; set; }

    /// <summary>今の書き出し設定</summary>
    public ExportSettingsData Export { get; set; } = new();

    /// <summary>切り抜きの比率の選択（全画像共通）</summary>
    public CropAspectData CropAspect { get; set; } = new();

    /// <summary>利用者のプリセット（組み込みのプリセットは保存しない）</summary>
    public List<PresetData> UserPresets { get; set; } = new();

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}

/// <summary>書き出し設定の保存する形</summary>
public sealed class ExportSettingsData
{
    /// <summary>ファイルサイズの上限の既定値（KB）。「上限を指定」に切り替えたときに最初に入っている値</summary>
    public const long DefaultSizeLimitKB = 300;

    /// <summary>上限（KB）に指定できる最小</summary>
    public const long MinSizeLimitKB = 1;

    /// <summary>上限（KB）に指定できる最大</summary>
    public const long MaxSizeLimitKB = 1_000_000;

    /// <summary>形式の Id（既定は JPEG）</summary>
    public string FormatId { get; set; } = "jpeg";

    /// <summary>形式の Id → その形式のオプション（形式を切り替えても、それぞれの値を覚えておく）</summary>
    public Dictionary<string, ParameterValues?> FormatOptions { get; set; } = new(StringComparer.Ordinal);

    /// <summary>品質の決め方が「ファイルサイズの上限を指定」か（false は「品質を指定」）</summary>
    public bool UseSizeLimit { get; set; }

    /// <summary>ファイルサイズの上限（KB。1 KB = 1024 バイト）。「品質を指定」のときも値は覚えておく</summary>
    public long SizeLimitKB { get; set; } = DefaultSizeLimitKB;

    /// <summary>書き出す大きさ</summary>
    public OutputSizeData Size { get; set; } = new();

    /// <summary>背景色（#RRGGBB）</summary>
    public string Background { get; set; } = "#FFFFFF";

    /// <summary>エンジンの書き出し設定にする（値は検証済みであること）</summary>
    public ExportSettings ToExportSettings() => new(
        FormatId,
        FormatOptions.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal),
        UseSizeLimit ? SizeLimitKB * 1024 : null,
        Size.ToOutputSize(),
        ColorText.Parse(Background));

    /// <summary>
    /// エンジンの書き出し設定から作る。上限は「品質を指定」のときも覚えておくので、そのときの値を rememberedSizeLimitKB で渡す
    /// </summary>
    public static ExportSettingsData From(ExportSettings settings, long rememberedSizeLimitKB = DefaultSizeLimitKB)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ExportSettingsData
        {
            FormatId = settings.FormatId,
            FormatOptions = settings.FormatOptions.ToDictionary(p => p.Key, p => (ParameterValues?)p.Value, StringComparer.Ordinal),
            UseSizeLimit = settings.SizeLimitBytes is not null,
            SizeLimitKB = settings.SizeLimitBytes is { } bytes ? bytes / 1024 : rememberedSizeLimitKB,
            Size = OutputSizeData.From(settings.Size),
            Background = ColorText.Format(settings.Background),
        };
    }
}

/// <summary>書き出す大きさの保存する形</summary>
public sealed class OutputSizeData
{
    public const string ExactMode = "exact";
    public const string LongSideMode = "longSide";
    public const string OriginalMode = "original";

    /// <summary>方式（"exact"・"longSide"・"original"）</summary>
    public string Mode { get; set; } = ExactMode;

    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 768;
    public int LongSide { get; set; } = OutputSize.DefaultLongSide;

    /// <summary>エンジンの書き出す大きさにする（値は検証済みであること）</summary>
    public OutputSize ToOutputSize() => new(ParseMode(Mode) ?? OutputSizeMode.Exact, Width, Height, LongSide);

    public static OutputSizeData From(OutputSize size)
    {
        ArgumentNullException.ThrowIfNull(size);
        return new OutputSizeData { Mode = FormatMode(size.Mode), Width = size.Width, Height = size.Height, LongSide = size.LongSide };
    }

    /// <summary>方式の文字列を読む。知らない文字列なら null</summary>
    public static OutputSizeMode? ParseMode(string? mode) => mode switch
    {
        ExactMode => OutputSizeMode.Exact,
        LongSideMode => OutputSizeMode.LongSide,
        OriginalMode => OutputSizeMode.Original,
        _ => null,
    };

    public static string FormatMode(OutputSizeMode mode) => mode switch
    {
        OutputSizeMode.Exact => ExactMode,
        OutputSizeMode.LongSide => LongSideMode,
        OutputSizeMode.Original => OriginalMode,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}

/// <summary>切り抜きの比率の保存する形</summary>
public sealed class CropAspectData
{
    public const string FreeKind = "free";
    public const string OriginalKind = "original";
    public const string FixedKind = "fixed";

    /// <summary>選び方（"free"・"original"・"fixed"）</summary>
    public string Kind { get; set; } = FreeKind;

    /// <summary>比率の幅（fixed のとき）</summary>
    public int Width { get; set; }

    /// <summary>比率の高さ（fixed のとき）</summary>
    public int Height { get; set; }

    /// <summary>エンジンの比率の選択にする。読めないときは null</summary>
    public CropAspect? ToCropAspect() => Kind switch
    {
        FreeKind => CropAspect.Free,
        OriginalKind => CropAspect.Original,
        FixedKind when Width > 0 && Height > 0 => CropAspect.Fixed(Width, Height),
        _ => null,
    };

    public static CropAspectData From(CropAspect aspect)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        return aspect.Kind switch
        {
            CropAspectKind.Free => new CropAspectData { Kind = FreeKind },
            CropAspectKind.Original => new CropAspectData { Kind = OriginalKind },
            CropAspectKind.Fixed => new CropAspectData { Kind = FixedKind, Width = aspect.Ratio.Width, Height = aspect.Ratio.Height },
            _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect.Kind, null),
        };
    }
}

/// <summary>利用者のプリセットの保存する形</summary>
public sealed class PresetData
{
    public string Name { get; set; } = "";

    public ExportSettingsData Export { get; set; } = new();

    /// <summary>エンジンのプリセットにする（値は検証済みであること）</summary>
    public ExportPreset ToPreset() => new(Name, Export.ToExportSettings(), IsBuiltIn: false);

    public static PresetData From(ExportPreset preset, long rememberedSizeLimitKB = ExportSettingsData.DefaultSizeLimitKB)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return new PresetData { Name = preset.Name, Export = ExportSettingsData.From(preset.Settings, rememberedSizeLimitKB) };
    }
}

/// <summary>色と "#RRGGBB" の変換</summary>
public static class ColorText
{
    /// <summary>"#RRGGBB" として読めるか（大文字小文字は問わない）</summary>
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (text is null || text.Length != 7 || text[0] != '#') return false;
        for (int i = 1; i < 7; i++)
        {
            if (!char.IsAsciiHexDigit(text[i])) return false;
        }
        color = new RgbColor(
            Convert.ToByte(text.Substring(1, 2), 16),
            Convert.ToByte(text.Substring(3, 2), 16),
            Convert.ToByte(text.Substring(5, 2), 16));
        return true;
    }

    /// <exception cref="FormatException">"#RRGGBB" として読めない</exception>
    public static RgbColor Parse(string text)
        => TryParse(text, out var color) ? color : throw new FormatException($"色として読めません: {text}");

    /// <summary>"#RRGGBB"（大文字）にする</summary>
    public static string Format(RgbColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
