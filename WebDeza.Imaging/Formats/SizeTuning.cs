namespace WebDeza.Imaging.Formats;

/// <summary>
/// ファイルサイズを調整できる整数のオプションの宣言。値が大きいほどファイルが大きくなるもの（JPEG の品質など）。
/// これを宣言した形式は、書き出しサイズの上限を指定できる（上限以下になるいちばん大きい値を探す）。
/// </summary>
/// <param name="ParameterKey">オプションのキー。Options にある Integer のオプションを指す</param>
public sealed record SizeTuning(string ParameterKey);
