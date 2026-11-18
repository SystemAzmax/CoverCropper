using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using WebDeza.Imaging.Formats;
using WebDeza.ViewModels;
using WebDeza.Views.Dialogs;

namespace WebDeza.Services;

/// <summary>ダイアログ（テストで差し替える）</summary>
public interface IDialogService
{
    /// <summary>エラーを出す</summary>
    void ShowError(string message);

    /// <summary>知らせる（情報のアイコン。M11 で失敗が無いとき）</summary>
    void ShowInfo(string message);

    /// <summary>注意を出す（警告のアイコン。M11 で失敗があったとき）</summary>
    void ShowWarning(string message);

    /// <summary>確認する（［はい］［いいえ］。既定のボタンは［いいえ］）。［はい］なら true</summary>
    bool Confirm(string message);

    /// <summary>確認する（［OK］［キャンセル］）。［OK］なら true（M05 など、続けて何かを選ばせるとき）</summary>
    bool ConfirmOkCancel(string message);

    /// <summary>フォルダを選ぶダイアログ。選んだフォルダのパス、やめたときは null</summary>
    /// <param name="initialDirectory">最初に開くフォルダ（無ければ既定）</param>
    string? ShowFolderDialog(string? initialDirectory);

    /// <summary>フォルダをエクスプローラーで開く（フォルダがあること）</summary>
    void OpenFolder(string folder);

    /// <summary>「開く」のダイアログ（複数選択可）。選んだファイルのパス、やめたときは null</summary>
    /// <param name="initialDirectory">最初に開くフォルダ（無ければ既定）</param>
    /// <param name="filter">種類の一覧（OpenFileDialog.Filter の書き方）</param>
    IReadOnlyList<string>? ShowOpenImageDialog(string? initialDirectory, string filter);

    /// <summary>画像サイズのダイアログ（モーダル）。OK なら true（変更後の大きさは viewModel.NewSize）</summary>
    bool ShowImageSizeDialog(ImageSizeDialogViewModel viewModel);

    /// <summary>
    /// 保存のダイアログ（モーダル）。保存の処理はダイアログの中で行い、保存できたら閉じて true（viewModel.Result に結果）。
    /// ［キャンセル］なら false
    /// </summary>
    bool ShowExportDialog(ExportDialogViewModel viewModel);
}

public sealed class DialogService : IDialogService
{
    /// <summary>
    /// メッセージなどの持ち主: 今アクティブなウィンドウ（保存のダイアログの上で出すときは、そのダイアログ）。無ければメインウィンドウ
    /// </summary>
    private static Window? Owner
        => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsLoaded)
           ?? Application.Current?.MainWindow;

    public void ShowError(string message)
        => Show(message, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message)
        => Show(message, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message)
        => Show(message, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>既定のボタンは［いいえ］（Enter を押しただけで、保存していない編集を捨てないように）</summary>
    public bool Confirm(string message)
        => Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public bool ConfirmOkCancel(string message)
        => Show(message, MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK;

    public string? ShowFolderDialog(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = "出力先フォルダを選択", Multiselect = false };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public void OpenFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

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

    public bool ShowImageSizeDialog(ImageSizeDialogViewModel viewModel)
    {
        var dialog = new ImageSizeDialog(viewModel);
        if (Owner is { IsLoaded: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    public bool ShowExportDialog(ExportDialogViewModel viewModel)
    {
        var dialog = new ExportDialog(viewModel);
        if (Owner is { IsLoaded: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    private static MessageBoxResult Show(string message, MessageBoxButton button, MessageBoxImage image,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var owner = Owner;
        return owner is { IsLoaded: true }
            ? MessageBox.Show(owner, message, AppInfo.ProductName, button, image, defaultResult)
            : MessageBox.Show(message, AppInfo.ProductName, button, image, defaultResult);
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
