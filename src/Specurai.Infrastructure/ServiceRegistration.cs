using Microsoft.Extensions.DependencyInjection;
using Specurai.Application.Services;
using Specurai.Domain.Interfaces;
using Specurai.Infrastructure.Repositories;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure;

/// <summary>
/// 共用服務註冊（Desktop、McpServer、Cli 三者共用）
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// 註冊所有核心服務（Repository + Application Service）
    /// </summary>
    public static IServiceCollection AddSpecuraiCore(this IServiceCollection services)
    {
        // Infrastructure - 連線管理器
        services.AddSingleton<IConnectionManager, ConnectionManager>();

        // Infrastructure - Repositories
        services.AddSingleton<ITableRepository>(sp =>
            new TableRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IColumnRepository>(sp =>
            new ColumnRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IIndexRepository>(sp =>
            new IndexRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IRelationRepository>(sp =>
            new RelationRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IParameterRepository>(sp =>
            new ParameterRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<ISqlQueryRepository>(sp =>
            new SqlQueryRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<ISqlDryRunRepository>(sp =>
            new SqlDryRunRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IColumnTypeRepository>(sp =>
            new ColumnTypeRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IDatabaseRecoveryModelRepository>(sp =>
            new DatabaseRecoveryModelRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

        // Application - Recovery Model（三端共用：Desktop / Cli / McpServer）
        services.AddSingleton<IDatabaseRecoveryModelService>(sp =>
            new DatabaseRecoveryModelService(sp.GetRequiredService<IDatabaseRecoveryModelRepository>()));

        // Application - 核心查詢服務
        services.AddSingleton<ITableQueryService, TableQueryService>();

        // Infrastructure - Schema 比對
        services.AddSingleton<ISchemaCollector, MssqlSchemaCollector>();
        services.AddSingleton<ISchemaCompareService, SchemaCompareService>();

        // Infrastructure - Schema Migration
        services.AddSingleton<ISchemaMigrationExecutor, SchemaMigrationExecutor>();

        // Application - Schema Migration
        services.AddSingleton<ISqlScriptGenerator, SqlScriptGenerator>();
        services.AddSingleton<ISchemaMigrationService, SchemaMigrationService>();

        // Infrastructure - 健康監控
        services.AddSingleton<IHealthMonitoringRepository>(sp =>
            new HealthMonitoringRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IHealthMonitoringInstaller>(sp =>
            new HealthMonitoringInstaller(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IHealthMonitoringService, HealthMonitoringService>();

        // Infrastructure - 效能診斷
        services.AddSingleton<IPerformanceDiagnosticsRepository>(sp =>
            new PerformanceDiagnosticsRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IPerformanceDiagnosticsService, PerformanceDiagnosticsService>();

        // Infrastructure - 欄位使用分析
        services.AddSingleton<IColumnUsageRepository>(sp =>
            new ColumnUsageRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IColumnUsageService, ColumnUsageService>();

        // Infrastructure - 表格統計
        services.AddSingleton<ITableStatisticsRepository>(sp =>
            new TableStatisticsRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<ITableStatisticsService, TableStatisticsService>();

        // Infrastructure - 使用狀態分析
        services.AddSingleton<IUsageAnalysisRepository>(sp =>
            new UsageAnalysisRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IUsageAnalysisService>(sp =>
            new UsageAnalysisService(
                sp.GetRequiredService<IUsageAnalysisRepository>(),
                sp.GetRequiredService<IConnectionManager>(),
                connStr => new UsageAnalysisRepository(() => connStr)));

        // Application - 跨資料庫欄位搜尋
        services.AddSingleton<IColumnSearchService>(sp =>
            new ColumnSearchService(
                sp.GetRequiredService<IConnectionManager>(),
                connStr => new SqlQueryRepository(() => connStr)));

        // Infrastructure - Agent Job
        services.AddSingleton<IDatabaseInfoRepository>(sp =>
            new DatabaseInfoRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IAgentJobRepository>(sp =>
            new AgentJobRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IAgentJobService, AgentJobService>();

        // Infrastructure - 維護計劃
        services.AddSingleton<IMaintenancePlanSqlGenerator, MaintenancePlanSqlGenerator>();
        services.AddSingleton<IMaintenancePlanService, MaintenancePlanService>();

        // Infrastructure - 匯出
        services.AddSingleton<IExportService>(sp =>
            new ExcelExportService(sp.GetRequiredService<ITableQueryService>()));
        services.AddSingleton<IConnectionExportService, ConnectionExportService>();

        // Infrastructure - 備份服務
        services.AddSingleton<IBackupService, MssqlBackupService>();

        return services;
    }
}
