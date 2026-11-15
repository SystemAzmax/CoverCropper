using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WebDeza.Composition;
using WebDeza.Core;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Models;
using WebDeza.Services;
using WebDeza.Views;

namespace WebDeza;

public partial class App : Application
{
    private AppLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 最初に、ログの場所（%APPDATA%\webDeza\logs\app.log）を決める。CoverCropper とは別のフォルダ
        var log = new AppLog(AppPaths.GetLogFile(AppInfo.ProductName));
        _log = log;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) log.Error("AppDomain unhandled exception", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        // 形式・フィルターの登録（足すときは AppModules に 1 行足す）
        var formats = new FormatRegistry();
        var filters = new FilterRegistry();
        AppModules.Register(formats, filters);

        // 設定（%APPDATA%\webDeza\settings.json）。形式の登録の後に読む（形式 Id とオプションの検証に使うため）
        var settingsService = new SettingsService(formats);
        var loaded = settingsService.Load();
        if (loaded.Messages.Count > 0)
            log.Write($"Settings: {string.Join(" / ", loaded.Messages)} ({settingsService.FilePath})");

        var window = new MainWindow(loaded.Settings, () => SaveSettingsQuietly(settingsService, loaded.Settings), loaded.Messages);
        MainWindow = window;
        window.Show();
    }

    /// <summary>設定を保存する。失敗してもアプリは止めず、ログに書く</summary>
    private void SaveSettingsQuietly(SettingsService service, AppSettings settings)
    {
        try
        {
            service.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Error("Settings save failed", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("Dispatcher unhandled exception", e.Exception);
        Mouse.OverrideCursor = null;
        MessageBox.Show(
            Messages.UnexpectedError(e.Exception.Message, _log?.LogFile ?? ""),
            AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
