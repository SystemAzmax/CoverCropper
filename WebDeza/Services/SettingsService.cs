using System.Text.Json;
using WebDeza.Core;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;
using WebDeza.Models;

namespace WebDeza.Services;

/// <summary>読み込んだ設定と、起動時にステータスバーに出すメッセージ（M20・M25）</summary>
/// <param name="Settings">検証済みの設定</param>
/// <param name="Messages">出すメッセージ（無ければ空）</param>
public sealed record SettingsLoadResult(AppSettings Settings, IReadOnlyList<string> Messages);

/// <summary>
/// 設定（%APPDATA%\webDeza\settings.json）の読み書きと、値の検証（設計書 F-12）。
/// </summary>
public sealed class SettingsService
{
    /// <summary>既定の形式（登録されていない形式のときに使う）</summary>
    public const string DefaultFormatId = "jpeg";

    /// <summary>背景色の既定値</summary>
    public const string DefaultBackground = "#FFFFFF";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new ParameterValuesJsonConverter() },
    };

    private readonly FormatRegistry _formats;
    private readonly JsonSettingsService<AppSettings> _file;

    /// <param name="formats">形式レジストリ（形式の Id とオプションの検証に使う）</param>
    public SettingsService(FormatRegistry formats)
        : this(formats, AppPaths.GetSettingsFile(AppInfo.ProductName)) { }

    /// <param name="formats">形式レジストリ（形式の Id とオプションの検証に使う）</param>
    /// <param name="path">設定ファイルのパス</param>
    public SettingsService(FormatRegistry formats, string path)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = formats;
        _file = new JsonSettingsService<AppSettings>(path, JsonOptions);
    }

    /// <summary>設定ファイルのパス</summary>
    public string FilePath => _file.FilePath;

    /// <summary>
    /// 設定を読み、値を検証して返す。ファイルが無ければ既定値。
    /// 壊れているときは既定値にして M20、登録されていない形式は JPEG にして M25（例外は投げない）
    /// </summary>
    public SettingsLoadResult Load()
    {
        var messages = new List<string>();
        var settings = _file.Load();
        if (_file.LastLoadError is not null)
            messages.Add(WebDeza.Messages.M20_SettingsLoadFailed);

        Validate(settings, messages);
        return new SettingsLoadResult(settings, messages);
    }

    /// <summary>設定を書く（一時ファイルに書いてから置き換える）</summary>
    public void Save(AppSettings settings) => _file.Save(settings);

    /// <summary>初回起動時の書き出しの設定（JPEG・品質 85・背景は白）</summary>
    public ExportSettingsData CreateDefaultExport()
    {
        var export = new ExportSettingsData();
        ValidateExport(export, new HashSet<string>(StringComparer.Ordinal), new List<string>());
        return export;
    }

    // ---------------------------------------------------------------- 検証

    /// <summary>
    /// 設定の値を検証して直す。登録されていない形式 Id は JPEG にして M25 を messages に足す。
    /// オプション・上限・背景色・比率が範囲外か読めないときは、その項目を既定値にする
    /// </summary>
    public void Validate(AppSettings settings, IList<string> messages)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(messages);

        var reported = new HashSet<string>(StringComparer.Ordinal); // M25 は形式 Id ごとに 1 回だけ出す

        settings.OutputFolder = NullIfBlank(settings.OutputFolder);
        settings.LastOpenFolder = NullIfBlank(settings.LastOpenFolder);

        settings.Export ??= new ExportSettingsData();
        ValidateExport(settings.Export, reported, messages);

        // 選択範囲の比率: 知らない選び方・範囲外の比率は「自由」
        settings.SelectionAspect = settings.SelectionAspect is { IsValid: true } aspect
            ? aspect.Kind == SelectionAspectData.FixedKind
                ? SelectionAspectData.Fixed(aspect.Width, aspect.Height)
                : new SelectionAspectData { Kind = aspect.Kind }
            : SelectionAspectData.Free();

        // ウィンドウの位置・大きさ: 数値として使えないものは無かったことにする（画面の中に収まるかはウィンドウが確かめる）
        settings.WindowLeft = FiniteOrNull(settings.WindowLeft);
        settings.WindowTop = FiniteOrNull(settings.WindowTop);
        settings.WindowWidth = PositiveOrNull(settings.WindowWidth);
        settings.WindowHeight = PositiveOrNull(settings.WindowHeight);
        settings.ExportDialogWidth = PositiveOrNull(settings.ExportDialogWidth);
        settings.ExportDialogHeight = PositiveOrNull(settings.ExportDialogHeight);
    }

    private void ValidateExport(ExportSettingsData export, ISet<string> reported, IList<string> messages)
    {
        // 形式: 書き出しのできる登録済みの形式でなければ JPEG（M25）
        var format = FindEncodable(export.FormatId);
        if (format is null)
        {
            var id = export.FormatId ?? "";
            if (reported.Add(id)) messages.Add(WebDeza.Messages.M25_FormatNotFound(id));
            format = FindEncodable(DefaultFormatId) ?? _formats.Encodable.FirstOrDefault();
            export.FormatId = format?.Id ?? DefaultFormatId;
        }

        // オプション: 書き出しのできる形式はすべて、記述に合う値をそろえる（範囲外の値は既定値）。
        // 登録されていない形式のオプションは、プラグインを戻したときのためにそのまま残す
        var options = new Dictionary<string, ParameterValues?>(StringComparer.Ordinal);
        foreach (var (id, values) in export.FormatOptions ?? new Dictionary<string, ParameterValues?>())
        {
            if (id is not null && values is not null && FindEncodable(id) is null) options[id] = values;
        }
        foreach (var encodable in _formats.Encodable)
        {
            ParameterValues? stored = null;
            export.FormatOptions?.TryGetValue(encodable.Id, out stored);
            options[encodable.Id] = ValidateOptions(stored, encodable.Encoder!.Options);
        }
        export.FormatOptions = options;

        if (export.SizeLimitKB is < ExportSettingsData.MinSizeLimitKB or > ExportSettingsData.MaxSizeLimitKB)
            export.SizeLimitKB = ExportSettingsData.DefaultSizeLimitKB;


        export.Background = ColorText.TryParse(export.Background, out var color)
            ? ColorText.Format(color)
            : DefaultBackground;
    }

    /// <summary>
    /// 記述ごとに値を確かめる。記述に合う値（範囲内・選択肢にある・種類が合う）はそのまま使い、
    /// 合わない値と無い値は既定値にする。記述に無いキーは捨てる
    /// </summary>
    private static ParameterValues ValidateOptions(ParameterValues? stored, IReadOnlyList<ParameterDescriptor> descriptors)
    {
        var result = ParameterValues.Defaults(descriptors);
        if (stored is null) return result;

        foreach (var descriptor in descriptors)
        {
            if (!stored.ContainsKey(descriptor.Key)) continue;
            object raw = stored[descriptor.Key];
            object coerced = descriptor.Coerce(raw);
            if (IsSameValue(descriptor, raw, coerced))
                result = result.With(descriptor.Key, coerced);
        }
        return result;
    }

    /// <summary>Coerce で値が変わらなかったか（丸められた・既定値に置き換えられた値は、範囲外として扱う）</summary>
    private static bool IsSameValue(ParameterDescriptor descriptor, object raw, object coerced)
    {
        if (descriptor.Kind == ParameterKind.Color)
            return raw is string text && string.Equals(text, (string)coerced, StringComparison.OrdinalIgnoreCase);
        return ParameterValues.Empty.With(descriptor.Key, raw) == ParameterValues.Empty.With(descriptor.Key, coerced);
    }


    private IImageFormat? FindEncodable(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var format = _formats.FindById(id);
        return format is { Encoder: not null } && format.Capabilities.HasFlag(FormatCapabilities.Encode) ? format : null;
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static double? FiniteOrNull(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    private static double? PositiveOrNull(double? value) => value is { } v && double.IsFinite(v) && v > 0 ? v : null;
}
