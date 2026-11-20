using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebDeza.Mcp;
using WebDeza.Mcp.Tools;

// webDeza の MCP サーバー（設計書 F-14・5.12）。MCP クライアント（Claude Code など）が子プロセスとして起動し、
// 標準入出力でやり取りする。画面は出さず、ネットワークにもつながない
var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });

// 標準出力は MCP の通信だけに使う。ログはすべて標準エラー出力に書く（クライアントのログに出る）
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(_ => new ImagingCatalog());
builder.Services
    .AddMcpServer(options => options.ServerInfo = new() { Name = McpServerInfo.Name, Version = McpServerInfo.Version })
    .WithStdioServerTransport()
    .WithTools<CatalogTools>(McpJson.Options);

await builder.Build().RunAsync();
