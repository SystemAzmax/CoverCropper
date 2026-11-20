using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Wic;

namespace WebDeza.Modules;

/// <summary>
/// 形式とフィルターの登録をまとめる所。足すときは、ここに 1 行足す（設計書 1.3）。
/// webDeza（AppModules から呼ぶ）と MCP サーバー（WebDeza.Mcp）が同じものを使う。
/// </summary>
public static class ImagingModules
{
    public static void Register(FormatRegistry formats, FilterRegistry filters)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(filters);

        formats.AddWicFormats();                 // 標準の形式（設計書 5.4）
        filters.Register(new BlurFilter());      // 標準のフィルター（設計書 F-06。編集パネルにこの順に並ぶ）
        filters.Register(new BasicAdjustFilter());
    }
}
