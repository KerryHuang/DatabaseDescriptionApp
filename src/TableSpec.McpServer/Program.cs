using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TableSpec.Application.Services;
using TableSpec.Domain.Interfaces;
using TableSpec.Infrastructure.Repositories;
using TableSpec.Infrastructure.Services;

var builder = Host.CreateApplicationBuilder(args);

// stdio 傳輸模式下，停用所有日誌輸出以避免干擾 JSON-RPC 通訊
builder.Logging.ClearProviders();

// 註冊 MCP Server（stdio 傳輸）
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Infrastructure - 連線管理器（共用桌面應用程式的連線設定）
builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();

// Infrastructure - Repositories
builder.Services.AddSingleton<ITableRepository>(sp =>
    new TableRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<IColumnRepository>(sp =>
    new ColumnRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<IIndexRepository>(sp =>
    new IndexRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<IRelationRepository>(sp =>
    new RelationRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<IParameterRepository>(sp =>
    new ParameterRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<ISqlQueryRepository>(sp =>
    new SqlQueryRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<IColumnTypeRepository>(sp =>
    new ColumnTypeRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

// Application - 服務
builder.Services.AddSingleton<ITableQueryService, TableQueryService>();

// Infrastructure - Schema 比對
builder.Services.AddSingleton<ISchemaCollector, MssqlSchemaCollector>();
builder.Services.AddSingleton<ISchemaCompareService, SchemaCompareService>();

// Infrastructure - 健康監控
builder.Services.AddSingleton<IHealthMonitoringRepository>(sp =>
    new HealthMonitoringRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<IHealthMonitoringService, HealthMonitoringService>();

// Infrastructure - 效能診斷
builder.Services.AddSingleton<IPerformanceDiagnosticsRepository>(sp =>
    new PerformanceDiagnosticsRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<IPerformanceDiagnosticsService, PerformanceDiagnosticsService>();

// Infrastructure - 欄位使用分析
builder.Services.AddSingleton<IColumnUsageRepository>(sp =>
    new ColumnUsageRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<IColumnUsageService, ColumnUsageService>();

// Infrastructure - 表格統計
builder.Services.AddSingleton<ITableStatisticsRepository>(sp =>
    new TableStatisticsRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<ITableStatisticsService, TableStatisticsService>();

// Infrastructure - 使用狀態分析
builder.Services.AddSingleton<IUsageAnalysisRepository>(sp =>
    new UsageAnalysisRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
builder.Services.AddSingleton<IUsageAnalysisService>(sp =>
    new UsageAnalysisService(
        sp.GetRequiredService<IUsageAnalysisRepository>(),
        sp.GetRequiredService<IConnectionManager>(),
        connStr => new UsageAnalysisRepository(() => connStr)));

// Application - 跨資料庫欄位搜尋
builder.Services.AddSingleton<IColumnSearchService>(sp =>
    new ColumnSearchService(
        sp.GetRequiredService<IConnectionManager>(),
        connStr => new SqlQueryRepository(() => connStr)));

await builder.Build().RunAsync();
