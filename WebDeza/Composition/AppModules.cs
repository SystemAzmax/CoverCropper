using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;
using WebDeza.Views.Canvas;

namespace WebDeza.Composition;

/// <summary>
/// 形式・フィルターなどの登録をまとめる所。足すときは、ここに 1 行足す（設計書 1.3・5.6）。
/// 道具は、最初に登録したものが既定の道具になる（「選択」は 8-5 で、手のひらの前に足す）。
/// </summary>
public static class AppModules
{
    public static void Register(FormatRegistry formats, FilterRegistry filters, ToolRegistry tools)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(tools);

        formats.AddWicFormats();                 // 標準の形式（設計書 5.4）
        filters.Register(new BlurFilter());      // 標準のフィルター（設計書 F-06。メニューと編集パネルにこの順に並ぶ）
        filters.Register(new BasicAdjustFilter());
        tools.Register(new HandToolFactory());   // 標準の道具（設計書 F-03。ボタンはこの順に並ぶ）
    }
}
