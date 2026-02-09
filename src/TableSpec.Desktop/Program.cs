using Avalonia;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using System;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Interfaces;
using TableSpec.Infrastructure.Repositories;
using TableSpec.Infrastructure.Services;
using Velopack;

namespace TableSpec.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack 初始化（必須在最前面）
        VelopackApp.Build().Run();

        // 配置 DI 容器
        var services = ConfigureServices();
        App.Services = services;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Infrastructure - Connection Manager (Singleton)
        services.AddSingleton<IConnectionManager, ConnectionManager>();

        // Infrastructure - Repositories (使用連線管理器取得連線字串)
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
        services.AddSingleton<IColumnTypeRepository>(sp =>
            new ColumnTypeRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

        // Application - Services
        services.AddSingleton<ITableQueryService, TableQueryService>();
        services.AddSingleton<IExportService, ExcelExportService>();

        // Infrastructure - Backup Service
        services.AddSingleton<IBackupService, MssqlBackupService>();

        // Infrastructure - Schema Compare Services
        services.AddSingleton<ISchemaCollector, MssqlSchemaCollector>();
        services.AddSingleton<ISchemaCompareService, SchemaCompareService>();

        // Infrastructure - Health Monitoring
        services.AddSingleton<IHealthMonitoringRepository>(sp =>
            new HealthMonitoringRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<IHealthMonitoringInstaller>(sp =>
            new HealthMonitoringInstaller(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

        // Application - Health Monitoring Service
        services.AddSingleton<IHealthMonitoringService, HealthMonitoringService>();

        // Infrastructure - Performance Diagnostics
        services.AddSingleton<IPerformanceDiagnosticsRepository>(sp =>
            new PerformanceDiagnosticsRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

        // Application - Performance Diagnostics Service
        services.AddSingleton<IPerformanceDiagnosticsService, PerformanceDiagnosticsService>();

        // Infrastructure - Column Usage
        services.AddSingleton<IColumnUsageRepository>(sp =>
            new ColumnUsageRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
        services.AddSingleton<ColumnUsageExcelExporter>();

        // Application - Column Usage Service
        services.AddSingleton<IColumnUsageService, ColumnUsageService>();

        // Infrastructure - Table Statistics
        services.AddSingleton<ITableStatisticsRepository>(sp =>
            new TableStatisticsRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

        // Application - Table Statistics Service
        services.AddSingleton<ITableStatisticsService, TableStatisticsService>();

        // Infrastructure - Usage Analysis
        services.AddSingleton<IUsageAnalysisRepository>(sp =>
            new UsageAnalysisRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

        // Application - Usage Analysis Service
        services.AddSingleton<IUsageAnalysisService>(sp =>
            new UsageAnalysisService(
                sp.GetRequiredService<IUsageAnalysisRepository>(),
                sp.GetRequiredService<IConnectionManager>(),
                connStr => new UsageAnalysisRepository(() => connStr)));

        // Application - Column Search Service（多資料庫搜尋）
        services.AddSingleton<IColumnSearchService>(sp =>
            new ColumnSearchService(
                sp.GetRequiredService<IConnectionManager>(),
                connStr => new SqlQueryRepository(() => connStr)));

        // ViewModels
        services.AddTransient<MainWindowViewModel>(sp =>
            new MainWindowViewModel(
                sp.GetRequiredService<IConnectionManager>(),
                sp.GetRequiredService<IExportService>(),
                sp.GetRequiredService<ITableQueryService>(),
                sp.GetRequiredService<ISqlQueryRepository>(),
                sp.GetRequiredService<IColumnTypeRepository>(),
                sp.GetRequiredService<ObjectTreeViewModel>()));
        services.AddTransient<ConnectionSetupViewModel>();
        services.AddTransient<ObjectTreeViewModel>();
        services.AddTransient<BackupRestoreDocumentViewModel>(sp =>
            new BackupRestoreDocumentViewModel(
                sp.GetRequiredService<IBackupService>(),
                sp.GetRequiredService<IConnectionManager>()));
        services.AddTransient<SchemaCompareDocumentViewModel>(sp =>
            new SchemaCompareDocumentViewModel(
                sp.GetRequiredService<ISchemaCompareService>(),
                sp.GetRequiredService<ISchemaCollector>(),
                sp.GetRequiredService<IConnectionManager>()));
        services.AddTransient<HealthMonitoringDocumentViewModel>(sp =>
            new HealthMonitoringDocumentViewModel(
                sp.GetRequiredService<IHealthMonitoringService>(),
                sp.GetRequiredService<IConnectionManager>()));
        services.AddTransient<PerformanceDiagnosticsDocumentViewModel>(sp =>
            new PerformanceDiagnosticsDocumentViewModel(
                sp.GetRequiredService<IPerformanceDiagnosticsService>()));
        services.AddTransient<ColumnUsageDocumentViewModel>(sp =>
            new ColumnUsageDocumentViewModel(
                sp.GetRequiredService<IColumnUsageService>(),
                sp.GetRequiredService<ColumnUsageExcelExporter>()));
        services.AddTransient<TableStatisticsDocumentViewModel>(sp =>
            new TableStatisticsDocumentViewModel(
                sp.GetRequiredService<ITableStatisticsService>()));
        services.AddTransient<MissingIndexReportDocumentViewModel>(sp =>
            new MissingIndexReportDocumentViewModel(
                sp.GetRequiredService<IPerformanceDiagnosticsService>()));
        services.AddTransient<UnusedIndexReportDocumentViewModel>(sp =>
            new UnusedIndexReportDocumentViewModel(
                sp.GetRequiredService<IPerformanceDiagnosticsService>()));
        services.AddTransient<UsageAnalysisDocumentViewModel>(sp =>
            new UsageAnalysisDocumentViewModel(
                sp.GetRequiredService<IUsageAnalysisService>(),
                sp.GetRequiredService<IConnectionManager>()));
        services.AddTransient<ColumnSearchDocumentViewModel>(sp =>
            new ColumnSearchDocumentViewModel(
                sp.GetRequiredService<ISqlQueryRepository>(),
                sp.GetRequiredService<IColumnTypeRepository>(),
                sp.GetRequiredService<IConnectionManager>(),
                sp.GetRequiredService<ITableQueryService>(),
                sp.GetRequiredService<IColumnSearchService>()));

        return services.BuildServiceProvider();
    }
}
