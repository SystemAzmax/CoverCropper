using System.ComponentModel;
using System.Windows;
using WebDeza.Models;

namespace WebDeza.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;

    /// <param name="settings">検証済みの設定（ウィンドウの位置の復元と保存に使う）</param>
    /// <param name="saveSettings">設定を保存する処理（閉じるときに呼ぶ）</param>
    /// <param name="startupMessages">起動時にステータスバーに出すメッセージ（M20・M25 など）</param>
    public MainWindow(AppSettings settings, Action saveSettings, IReadOnlyList<string> startupMessages)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(saveSettings);
        ArgumentNullException.ThrowIfNull(startupMessages);

        InitializeComponent();
        _settings = settings;
        _saveSettings = saveSettings;
        StatusTextBlock.Text = string.Join(" ／ ", startupMessages);
        RestorePlacement();
    }

    // ---------------------------------------------------------------- ウィンドウの位置の保存・復元

    private void RestorePlacement()
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (WindowPlacement.GetRestoreBounds(_settings, screen, MinWidth, MinHeight) is not { } bounds)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        var normal = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        WindowPlacement.Save(_settings, WindowState, normal);
        _saveSettings();
    }
}
