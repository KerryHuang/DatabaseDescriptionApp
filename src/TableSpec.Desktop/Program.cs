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

        return services.BuildServiceProvider();
    }
}
