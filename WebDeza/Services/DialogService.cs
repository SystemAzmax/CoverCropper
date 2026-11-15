using System.IO;
using System.Windows;
using Microsoft.Win32;
using WebDeza.Imaging.Formats;

namespace WebDeza.Services;

/// <summary>ダイアログ（テストで差し替える）</summary>
public interface IDialogService
{
    /// <summary>エラーを出す</summary>
    void ShowError(string message);

    /// <summary>「開く」のダイアログ（複数選択可）。選んだファイルのパス、やめたときは null</summary>
    /// <param name="initialDirectory">最初に開くフォルダ（無ければ既定）</param>
    /// <param name="filter">種類の一覧（OpenFileDialog.Filter の書き方）</param>
    IReadOnlyList<string>? ShowOpenImageDialog(string? initialDirectory, string filter);
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public void ShowError(string message)
        => Show(message, MessageBoxButton.OK, MessageBoxImage.Error);

    public IReadOnlyList<string>? ShowOpenImageDialog(string? initialDirectory, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = "画像を開く",
            Filter = filter,
            Multiselect = true,
            CheckFileExists = true,
        };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : null;
    }

    private static MessageBoxResult Show(string message, MessageBoxButton button, MessageBoxImage image)
    {
        var owner = Owner;
        return owner is { IsLoaded: true }
            ? MessageBox.Show(owner, message, AppInfo.ProductName, button, image)
            : MessageBox.Show(message, AppInfo.ProductName, button, image);
    }
}

/// <summary>「開く」のダイアログの種類の一覧を、形式レジストリの登録内容から作る（設計書 F-01）</summary>
public static class OpenFileFilter
{
    /// <summary>
    /// 先頭に「画像ファイル」（読める形式のすべての拡張子）、続けて形式ごと（登録順）、最後に「すべてのファイル」。
    /// 例: 画像ファイル|*.jpg;*.png|JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png|すべてのファイル|*.*
    /// </summary>
    public static string Build(FormatRegistry formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        var parts = new List<string>();
        var all = formats.Decodable.SelectMany(f => f.Extensions).Select(Pattern).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (all.Count > 0)
            parts.Add($"画像ファイル|{string.Join(";", all)}");

        foreach (var format in formats.Decodable)
        {
            var patterns = string.Join(";", format.Extensions.Select(Pattern));
            parts.Add($"{format.DisplayName} ({patterns})|{patterns}");
        }

        parts.Add("すべてのファイル|*.*");
        return string.Join("|", parts);

        static string Pattern(string extension) => "*" + (extension.StartsWith('.') ? extension : "." + extension);
    }
}
