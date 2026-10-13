using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CoverCropper.Services;
using CoverCropper.ViewModels;
using CoverCropper.Views;

namespace CoverCropper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) AppLog.Error("AppDomain unhandled exception", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        var viewModel = new MainViewModel(
            new ImageLoader(),
            new ImageResizer(),
            new ImageCropper(),
            new JpegSaver(),
            new OutputFileNamer(),
            new SettingsService(),
            new DialogService());

        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();

        // コマンドライン引数で画像を渡された場合（「送る」やアイコンへのドロップ）
        if (e.Args.Length > 0)
            _ = viewModel.DropAsync(e.Args);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Dispatcher unhandled exception", e.Exception);
        Mouse.OverrideCursor = null;
        MessageBox.Show(
            $"予期しないエラーが発生しました。\n{e.Exception.Message}\n\n詳細はログを確認してください:\n{AppPaths.LogFile}",
            "CoverCropper", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
