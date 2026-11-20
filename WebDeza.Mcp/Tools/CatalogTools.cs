using System.ComponentModel;
using ModelContextProtocol.Server;

namespace WebDeza.Mcp.Tools;

/// <summary>形式とフィルターの一覧を返すツール（設計書 F-14）</summary>
[McpServerToolType]
public sealed class CatalogTools(ImagingCatalog catalog)
{
    [McpServerTool(Name = "list_formats", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("webDeza で開ける・保存できる画像の形式の一覧を返す。保存するときに指定する形式の ID と、形式ごとのオプション（JPEG の品質など）の範囲と既定値が分かる。")]
    public IReadOnlyList<FormatInfo> ListFormats()
        => catalog.Formats.All.Select(CatalogInfo.Describe).ToArray();

    [McpServerTool(Name = "list_filters", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("webDeza でかけられるフィルター（ぼかしなど）の一覧を返す。フィルターの ID と、パラメーターの種類・範囲・既定値が分かる。")]
    public IReadOnlyList<FilterInfo> ListFilters()
        => catalog.Filters.All.Select(CatalogInfo.Describe).ToArray();
}
