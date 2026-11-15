using CoverCropper.Models;
using WebDeza.Core;

namespace CoverCropper.Services;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);

    /// <summary>直近の Load で設定ファイルが読めなかった場合のエラー内容（正常時 null）</summary>
    string? LastLoadError { get; }
}

/// <summary>%APPDATA%\CoverCropper\settings.json の読み書き（共通の JsonSettingsService に任せる）</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly JsonSettingsService<AppSettings> _service;

    public SettingsService() : this(System.IO.Path.Combine(AppPaths.DataFolder, WebDeza.Core.AppPaths.SettingsFileName)) { }

    public SettingsService(string path) => _service = new JsonSettingsService<AppSettings>(path);

    public string? LastLoadError => _service.LastLoadError;

    public AppSettings Load() => _service.Load();

    public void Save(AppSettings settings) => _service.Save(settings);
}
