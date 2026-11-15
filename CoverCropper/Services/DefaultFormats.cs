using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;

namespace CoverCropper.Services;

/// <summary>
/// CoverCropper が使う既定の形式レジストリ（WIC の形式を登録したもの）。
/// 引数なしで作った ImageLoader と JpegSaver がこれを使う。
/// </summary>
internal static class DefaultFormats
{
    public static FormatRegistry Registry { get; } = Create();

    private static FormatRegistry Create()
    {
        var registry = new FormatRegistry();
        registry.AddWicFormats();
        return registry;
    }
}
