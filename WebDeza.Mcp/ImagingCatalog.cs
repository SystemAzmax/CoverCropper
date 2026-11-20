using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Modules;

namespace WebDeza.Mcp;

/// <summary>
/// MCP サーバーで使う形式とフィルター。webDeza と同じ ImagingModules で登録する（設計書 5.12）。
/// </summary>
public sealed class ImagingCatalog
{
    /// <summary>ImagingModules で登録したもの</summary>
    public ImagingCatalog()
    {
        ImagingModules.Register(Formats, Filters);
    }

    /// <summary>登録済みの形式とフィルターを使う（テストで差し替えるため）</summary>
    public ImagingCatalog(FormatRegistry formats, FilterRegistry filters)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(filters);
        Formats = formats;
        Filters = filters;
    }

    public FormatRegistry Formats { get; } = new();

    public FilterRegistry Filters { get; } = new();
}
