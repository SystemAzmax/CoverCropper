using System.Text.Json;

namespace WebDeza.Core;

/// <summary>
/// JSON ファイルへの設定の読み書き。設定の型はアプリが決める（CoverCropper の SettingsService を一般化したもの）。
/// </summary>
/// <typeparam name="T">設定の型。ファイルが無い・読めないときは new T() を返す</typeparam>
public sealed class JsonSettingsService<T> where T : class, new()
{
    private static readonly JsonSerializerOptions DefaultOptions = new() { WriteIndented = true };

    private readonly JsonSerializerOptions _options;

    /// <param name="path">設定ファイルのパス</param>
    /// <param name="options">JSON の書き方。省略するとインデントつき</param>
    public JsonSettingsService(string path, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        FilePath = path;
        _options = options ?? DefaultOptions;
    }

    /// <summary>設定ファイルのパス</summary>
    public string FilePath { get; }

    /// <summary>直近の Load で設定ファイルが読めなかった場合のエラー内容（正常時 null）</summary>
    public string? LastLoadError { get; private set; }

    /// <summary>
    /// 設定を読む。ファイルが無ければ既定値（LastLoadError は null）、
    /// 壊れている・読めないときも既定値を返し、LastLoadError に理由を入れる（例外は投げない）
    /// </summary>
    public T Load()
    {
        LastLoadError = null;
        if (!File.Exists(FilePath)) return new T();

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<T>(json, _options) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastLoadError = ex.Message;
            return new T();
        }
    }

    /// <summary>
    /// 設定を書く。フォルダが無ければ作り、一時ファイル（{パス}.tmp）に書いてから置き換えるので、
    /// 途中で失敗しても壊れた設定ファイルは残らない
    /// </summary>
    public void Save(T settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var folder = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(folder);

        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, _options));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
