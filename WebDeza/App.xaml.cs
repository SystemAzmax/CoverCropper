using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WebDeza.Composition;
using WebDeza.Core;
using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Services;
using WebDeza.ViewModels;
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

        // 設定は形式の登録の後に読む（形式 Id とオプションの検証に使うため）
        var viewModel = new MainViewModel(formats, new SettingsService(formats), new DialogService(), log);

        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();

        // コマンドライン引数で渡されたファイル・フォルダを開く（「送る」や、アイコンへのドロップ。設計書 F-01）
        if (e.Args.Length > 0)
            _ = viewModel.OpenPathsAsync(e.Args);
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
