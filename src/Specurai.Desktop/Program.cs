using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using System;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Interfaces;
using Specurai.Infrastructure;
using Specurai.Infrastructure.Services;
using Velopack;

namespace Specurai.Desktop;

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

        // 註冊所有核心服務（共用）
        services.AddSpecuraiCore();

        // Desktop 特有：ColumnUsageExcelExporter
        services.AddSingleton<ColumnUsageExcelExporter>();

        // ViewModels
        services.AddTransient<MaintenancePlanDocumentViewModel>(sp =>
            new MaintenancePlanDocumentViewModel(
                sp.GetRequiredService<IAgentJobService>(),
                sp.GetRequiredService<IMaintenancePlanService>(),
                sp.GetRequiredService<IMaintenancePlanSqlGenerator>(),
                sp.GetRequiredService<IConnectionManager>()));

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
