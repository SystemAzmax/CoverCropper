using System.Reflection;

namespace WebDeza.Mcp;

/// <summary>MCP クライアントに知らせるサーバーの名前と版（設計書 5.12）</summary>
public static class McpServerInfo
{
    /// <summary>サーバーの名前。製品名と同じ（設計書 5.9）</summary>
    public const string Name = "webDeza";

    /// <summary>サーバーの版（アセンブリの版。"1.0.0" の形）</summary>
    public static string Version { get; } =
        typeof(McpServerInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
