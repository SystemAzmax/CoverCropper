using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using WebDeza.Imaging.Editing;
using WebDeza.Models;

namespace WebDeza.ViewModels;

/// <summary>
/// 画像一覧の 1 件（設計書 5.6）。画像の画素は持たず（表示中の画像は MainViewModel が持つ）、
/// パス・状態・編集内容（レシピの履歴）を持つ。
/// </summary>
public sealed partial class ImageItemViewModel : ObservableObject
{
    /// <param name="path">元画像のフルパス</param>
    /// <param name="time">履歴のまとめの判定に使う時刻</param>
    public ImageItemViewModel(string path, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(time);
        Path = path;
        History = new EditHistory(EditRecipe.Initial, time);
    }

    /// <summary>元画像のフルパス</summary>
    public string Path { get; }

    /// <summary>ファイル名（拡張子つき）</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>元ファイルのバイト数（書き出しサイズとの比率の表示に使う。読み込めるまでは 0）</summary>
    [ObservableProperty]
    private long _sourceFileSize;

    /// <summary>状態</summary>
    [ObservableProperty]
    private ImageItemStatus _status = ImageItemStatus.NotExported;

    /// <summary>エラーの理由（状態が Error のとき。一覧のツールチップに出す）</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>この画像の編集内容の履歴（元に戻す / やり直しは画像ごと）</summary>
    public EditHistory History { get; }

    /// <summary>エラーにする</summary>
    public void SetError(string message)
    {
        ErrorMessage = message;
        Status = ImageItemStatus.Error;
    }

    /// <summary>読み込めたときに呼ぶ。エラーを消し、元ファイルのバイト数を覚える</summary>
    public void SetLoaded(long sourceFileSize)
    {
        SourceFileSize = sourceFileSize;
        ErrorMessage = null;
        if (Status == ImageItemStatus.Error) Status = ImageItemStatus.NotExported;
    }

    /// <summary>元ファイルのバイト数。読めなければ 0</summary>
    internal static long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
