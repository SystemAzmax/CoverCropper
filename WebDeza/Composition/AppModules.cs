using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Modules;
using WebDeza.Views.Canvas;

namespace WebDeza.Composition;

/// <summary>
/// webDeza の登録をまとめる所（設計書 1.3・5.6）。形式とフィルターは ImagingModules（MCP サーバーと共用）に足し、
/// 道具はここに 1 行足す。道具は、最初に登録したものが既定の道具になる。
/// </summary>
public static class AppModules
{
    public static void Register(FormatRegistry formats, FilterRegistry filters, ToolRegistry tools)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(tools);

        ImagingModules.Register(formats, filters); // 形式とフィルター（設計書 5.4・F-06）
        tools.Register(new SelectToolFactory());   // 標準の道具（設計書 F-03。ボタンはこの順に並び、最初が既定）
        tools.Register(new HandToolFactory());
    }
}
