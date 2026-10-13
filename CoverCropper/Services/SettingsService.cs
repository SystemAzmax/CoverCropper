using System.IO;
using System.Text.Json;
using CoverCropper.Models;

namespace CoverCropper.Services;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);

    /// <summary>直近の Load で設定ファイルが読めなかった場合のエラー内容（正常時 null）</summary>
    string? LastLoadError { get; }
}

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsService() : this(Path.Combine(AppPaths.DataFolder, "settings.json")) { }

    public SettingsService(string path) => _path = path;

    public string? LastLoadError { get; private set; }

    public AppSettings Load()
    {
        LastLoadError = null;
        if (!File.Exists(_path)) return new AppSettings();

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastLoadError = ex.Message;
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var folder = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(folder);

        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, _path, overwrite: true);
    }
}
