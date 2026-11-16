using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WebDeza.Imaging;

namespace WebDeza.ViewModels;

/// <summary>
/// 画像サイズのダイアログ（設計書 F-05・4.6）。今の大きさを出し、変更後の幅と高さ（px）を入力させる。
/// 「縦横比を保つ」がオンなら、片方を入れるともう片方を今の画像の縦横比から決める。変更後の大きさと倍率（%）を出す。
/// </summary>
/// <remarks>
/// 入力欄は打つたびに当てはめる（もう片方の欄と、変更後の表示がすぐ変わるように）。
/// 打っている欄の文字は書き換えない（全角の数字なども、そのまま残す）。
/// </remarks>
public sealed partial class ImageSizeDialogViewModel : ObservableObject
{
    /// <summary>幅と高さの上限（px。設計書 F-05）</summary>
    public const int MaxSide = 30000;

    /// <summary>幅・高さが整数でないか、1〜MaxSide に入らないときの表示</summary>
    public const string OutOfRangeText = "幅と高さは 1〜30000 px の整数で入力してください。";

    /// <summary>幅 × 高さ × 4 が配列の上限を超えるときの表示</summary>
    public const string TooLargeText = "大きすぎます（幅 × 高さが上限を超えます）。";

    private string _widthText;
    private string _heightText;
    private bool _keepAspect = true;

    /// <param name="currentWidth">今の画像の幅</param>
    /// <param name="currentHeight">今の画像の高さ</param>
    public ImageSizeDialogViewModel(int currentWidth, int currentHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentHeight);
        CurrentWidth = currentWidth;
        CurrentHeight = currentHeight;
        _widthText = Format(currentWidth);
        _heightText = Format(currentHeight);
    }

    /// <summary>今の画像の幅</summary>
    public int CurrentWidth { get; }

    /// <summary>今の画像の高さ</summary>
    public int CurrentHeight { get; }

    /// <summary>「今の大きさ」の表示（例: 6000 × 4000 px）</summary>
    public string CurrentSizeText => $"{Format(CurrentWidth)} × {Format(CurrentHeight)} px";

    /// <summary>変更後の幅の入力欄。縦横比を保つなら、正しい値を入れると高さも決める</summary>
    public string WidthText
    {
        get => _widthText;
        set
        {
            if (!SetProperty(ref _widthText, value ?? "")) return;
            if (KeepAspect && TryParseSide(_widthText, out int width))
                SetOther(ref _heightText, Scale(width, CurrentHeight, CurrentWidth), nameof(HeightText));
            OnSizeChanged();
        }
    }

    /// <summary>変更後の高さの入力欄。縦横比を保つなら、正しい値を入れると幅も決める</summary>
    public string HeightText
    {
        get => _heightText;
        set
        {
            if (!SetProperty(ref _heightText, value ?? "")) return;
            if (KeepAspect && TryParseSide(_heightText, out int height))
                SetOther(ref _widthText, Scale(height, CurrentWidth, CurrentHeight), nameof(WidthText));
            OnSizeChanged();
        }
    }

    /// <summary>「縦横比を保つ」（既定はオン）。オンにしたときは、幅から高さを決め直す</summary>
    public bool KeepAspect
    {
        get => _keepAspect;
        set
        {
            if (!SetProperty(ref _keepAspect, value)) return;
            if (value && TryParseSide(_widthText, out int width))
            {
                SetOther(ref _heightText, Scale(width, CurrentHeight, CurrentWidth), nameof(HeightText));
                OnSizeChanged();
            }
        }
    }

    /// <summary>変更後の大きさ。入力が正しくなければ null</summary>
    public (int Width, int Height)? NewSize
        => TryParseSide(_widthText, out int width) && TryParseSide(_heightText, out int height) && !IsTooLarge(width, height)
            ? (width, height)
            : null;

    /// <summary>OK できるか（変更後の大きさが正しい）</summary>
    public bool CanAccept => NewSize is not null;

    /// <summary>
    /// 「変更後」の表示: 「1920 × 1280 px（32%）」。縦と横の倍率が違えば「（幅 50%・高さ 100%）」。
    /// 入力が正しくなければ、その理由
    /// </summary>
    public string ResultText
    {
        get
        {
            if (NewSize is { } size)
            {
                string w = MainViewModel.FormatZoomPercent(size.Width * 100.0 / CurrentWidth);
                string h = MainViewModel.FormatZoomPercent(size.Height * 100.0 / CurrentHeight);
                string ratio = w == h ? w : $"幅 {w}・高さ {h}";
                return $"{Format(size.Width)} × {Format(size.Height)} px（{ratio}）";
            }
            return TryParseSide(_widthText, out int width) && TryParseSide(_heightText, out int height) && IsTooLarge(width, height)
                ? TooLargeText
                : OutOfRangeText;
        }
    }

    /// <summary>幅か高さの入力（整数で 1〜MaxSide。全角の数字も受け付ける）</summary>
    private static bool TryParseSide(string text, out int value)
        => SelectionPanelViewModel.TryParseInput(text, out value) && value is >= 1 and <= MaxSide;

    /// <summary>幅 × 高さ × 4 が配列の上限を超えるか（ImageBuffer に入らない）</summary>
    private static bool IsTooLarge(int width, int height)
    {
        try
        {
            ImageBuffer.GetByteLength(width, height);
            return false;
        }
        catch (ImageTooLargeException)
        {
            return true;
        }
    }

    /// <summary>value × numerator ÷ denominator を四捨五入した値（最低 1）</summary>
    private static int Scale(int value, int numerator, int denominator)
        => (int)Math.Max(1, Math.Min(int.MaxValue, Math.Round((double)value * numerator / denominator, MidpointRounding.AwayFromZero)));

    /// <summary>もう片方の欄を、求めた値にする</summary>
    private void SetOther(ref string field, int value, string propertyName)
    {
        string text = Format(value);
        if (field == text) return;
        field = text;
        OnPropertyChanged(propertyName);
    }

    private void OnSizeChanged()
    {
        OnPropertyChanged(nameof(NewSize));
        OnPropertyChanged(nameof(CanAccept));
        OnPropertyChanged(nameof(ResultText));
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
