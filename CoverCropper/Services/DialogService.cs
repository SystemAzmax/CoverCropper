using System.Windows;
using Microsoft.Win32;

namespace CoverCropper.Services;

public interface IDialogService
{
    void ShowError(string message);
    bool ConfirmOkCancel(string message);
    bool ConfirmYesNo(string message);
    string? ShowOpenImageDialog(string? initialDirectory);
    string? ShowFolderDialog(string? initialDirectory);
}

public sealed class DialogService : IDialogService
{
    private const string Caption = "CoverCropper";

    private static Window? Owner => Application.Current?.MainWindow;

    public void ShowError(string message)
        => Show(message, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool ConfirmOkCancel(string message)
        => Show(message, MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK;

    public bool ConfirmYesNo(string message)
        => Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? ShowOpenImageDialog(string? initialDirectory)
    {
        var patterns = string.Join(";", AppConstants.SupportedExtensions.Select(e => "*" + e));
        var dialog = new OpenFileDialog
        {
            Title = "画像を開く",
            Filter = $"画像ファイル|{patterns}|すべてのファイル|*.*",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (!string.IsNullOrEmpty(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? ShowFolderDialog(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = "保存先フォルダを選択", Multiselect = false };
        if (!string.IsNullOrEmpty(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    private static MessageBoxResult Show(string message, MessageBoxButton button, MessageBoxImage image)
    {
        var owner = Owner;
        return owner is { IsLoaded: true }
            ? MessageBox.Show(owner, message, Caption, button, image)
            : MessageBox.Show(message, Caption, button, image);
    }
}
