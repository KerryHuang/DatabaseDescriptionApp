using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Specurai.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

// stdio 傳輸模式下，停用所有日誌輸出以避免干擾 JSON-RPC 通訊
builder.Logging.ClearProviders();

// 註冊 MCP Server（stdio 傳輸）
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// 註冊所有核心服務（共用）
builder.Services.AddSpecuraiCore();

await builder.Build().RunAsync();
