using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;

namespace WebDeza.Composition;

/// <summary>
/// 形式・フィルターなどの登録をまとめる所。足すときは、ここに 1 行足す（設計書 1.3・5.6）。
/// 道具とプリセットの登録は、その仕組みを作るステップで引数に足す。
/// </summary>
public static class AppModules
{
    public static void Register(FormatRegistry formats, FilterRegistry filters)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(filters);

        formats.AddWicFormats();                 // 標準の形式（設計書 5.4）
        filters.Register(new BlurFilter());      // 標準のフィルター（設計書 F-06。メニューと編集パネルにこの順に並ぶ）
        filters.Register(new BasicAdjustFilter());
    }
}
