using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WebDeza.ViewModels;

namespace WebDeza.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.ExitRequested += (_, _) => Close();
        RestorePlacement();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 読み込み中・書き出し中は待機カーソル（設計書 4.2）
        if (e.PropertyName == nameof(MainViewModel.IsBusy))
            Mouse.OverrideCursor = _vm.IsBusy ? Cursors.Wait : null;
    }

    // ---------------------------------------------------------------- ウィンドウの位置の保存・復元

    private void RestorePlacement()
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (WindowPlacement.GetRestoreBounds(_vm.Settings, screen, MinWidth, MinHeight) is not { } bounds)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (_vm.Settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        var normal = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        WindowPlacement.Save(_vm.Settings, WindowState, normal);
        _vm.SaveSettingsQuietly();
    }
}
