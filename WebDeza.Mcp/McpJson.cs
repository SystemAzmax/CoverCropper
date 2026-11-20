using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol;

namespace WebDeza.Mcp;

/// <summary>ツールの結果を JSON にするときの設定（設計書 5.12）</summary>
public static class McpJson
{
    /// <summary>
    /// MCP の SDK の既定（キーは camelCase）に、日本語をエスケープしない設定を足したもの。
    /// 結果は MCP クライアントが JSON の文字列として受け取るので、HTML 向けのエスケープは要らない
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.MakeReadOnly();
        return options;
    }
}
