using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace WebDeza.Mcp.Tests;

/// <summary>ビルドした webDeza.Mcp.exe を子プロセスとして起動し、MCP クライアントとして標準入出力でやり取りする</summary>
public class StdioServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static string ServerPath()
    {
        // テストのプロジェクトが参照しているので、テストの出力フォルダに exe が置かれる
        string path = Path.Combine(AppContext.BaseDirectory, "webDeza.Mcp.exe");
        Assert.True(File.Exists(path), $"MCP サーバーの exe がありません: {path}");
        return path;
    }

    private static Task<McpClient> ConnectAsync(CancellationToken cancellationToken)
        => McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions { Name = "webDeza", Command = ServerPath() }),
            cancellationToken: cancellationToken);

    [Fact]
    public async Task Server_AnswersOverStdio_WithNameToolsAndUnescapedJson()
    {
        // S04: 標準出力が MCP の通信だけになっていて（ログが混ざると、クライアントが読めずに失敗する）、
        // サーバーの名前・ツールの一覧・ツールの結果が返る
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ConnectAsync(cts.Token);

        Assert.Equal(McpServerInfo.Name, client.ServerInfo?.Name);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
        Assert.Contains(tools, t => t.Name == "list_formats");
        Assert.Contains(tools, t => t.Name == "list_filters");
        Assert.All(tools.Where(t => t.Name.StartsWith("list_", StringComparison.Ordinal)),
            t => Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint));

        var result = await client.CallToolAsync("list_formats", cancellationToken: cts.Token);
        Assert.NotEqual(true, result.IsError);
        string text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

        // 日本語はエスケープせずに返す（AI が読みやすいように）
        Assert.Contains("品質", text);
        using var json = JsonDocument.Parse(text);
        var ids = json.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToArray();
        Assert.Equal("jpeg", ids[0]);
        Assert.Contains("png", ids);
    }
}
