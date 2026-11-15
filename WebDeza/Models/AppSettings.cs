using System.Text.Json.Serialization;
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

    /// <summary>書き出しの設定（書き出しのダイアログで、前回［書き出し］を押したときのもの）</summary>
    public ExportSettingsData Export { get; set; } = new();

    /// <summary>選択範囲の比率（全画像共通）</summary>
    public SelectionAspectData SelectionAspect { get; set; } = new();

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}

/// <summary>書き出しの設定の保存する形。大きさは持たない（今の画像の大きさのまま書き出す。設計書 F-08）</summary>
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

    /// <summary>背景色（#RRGGBB）</summary>
    public string Background { get; set; } = "#FFFFFF";

    /// <summary>エンジンの書き出しの設定にする（値は検証済みであること）</summary>
    public ExportSettings ToExportSettings() => new(
        FormatId,
        FormatOptions.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal),
        UseSizeLimit ? SizeLimitKB * 1024 : null,
        ColorText.Parse(Background));

    /// <summary>
    /// エンジンの書き出しの設定から作る。上限は「品質を指定」のときも覚えておくので、そのときの値を rememberedSizeLimitKB で渡す
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
            Background = ColorText.Format(settings.Background),
        };
    }
}

/// <summary>
/// 選択範囲の比率の保存する形（設計書 F-04）。
/// 1:1・4:3・3:2・16:9 とカスタムは、どれも "fixed" と幅・高さで持つ（縦横を入れ替えたものは、幅と高さが入れ替わる）
/// </summary>
public sealed class SelectionAspectData
{
    public const string FreeKind = "free";
    public const string OriginalKind = "original";
    public const string FixedKind = "fixed";

    /// <summary>カスタムの比率に入れられる最大（幅・高さとも 1〜これ）</summary>
    public const int MaxRatioLength = 10000;

    /// <summary>選び方（"free"・"original"・"fixed"）</summary>
    public string Kind { get; set; } = FreeKind;

    /// <summary>比率の幅（fixed のとき）</summary>
    public int Width { get; set; }

    /// <summary>比率の高さ（fixed のとき）</summary>
    public int Height { get; set; }

    /// <summary>値として使えるか（知らない選び方や、範囲外の比率でないか）。設定ファイルには書かない</summary>
    [JsonIgnore]
    public bool IsValid => Kind switch
    {
        FreeKind or OriginalKind => true,
        FixedKind => Width is >= 1 and <= MaxRatioLength && Height is >= 1 and <= MaxRatioLength,
        _ => false,
    };

    /// <summary>
    /// 画像に当てはめた比率。自由（と、使えない値）なら null、元画像と同じなら画像の幅:高さ
    /// </summary>
    public AspectRatio? ToAspectRatio(int imageWidth, int imageHeight)
    {
        if (!IsValid) return null;
        return Kind switch
        {
            OriginalKind => new AspectRatio(imageWidth, imageHeight),
            FixedKind => new AspectRatio(Width, Height),
            _ => null,
        };
    }

    public static SelectionAspectData Free() => new() { Kind = FreeKind };

    public static SelectionAspectData Original() => new() { Kind = OriginalKind };

    public static SelectionAspectData Fixed(int width, int height) => new() { Kind = FixedKind, Width = width, Height = height };
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
