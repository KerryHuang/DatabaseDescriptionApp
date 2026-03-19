using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Specurai.Application.Services;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// 健康監控安裝器實作
/// </summary>
public class HealthMonitoringInstaller : IHealthMonitoringInstaller
{
    private readonly Func<string?> _connectionStringProvider;

    public HealthMonitoringInstaller(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    /// <inheritdoc/>
    public async Task<InstallResult> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
        {
            return new InstallResult(false, "無法取得連線字串");
        }

        try
        {
            progress?.Report(new InstallProgress(0, "讀取安裝腳本..."));
            ct.ThrowIfCancellationRequested();

            var script = await LoadEmbeddedScriptAsync("HealthMonitoringInstall.sql");
            if (string.IsNullOrEmpty(script))
            {
                return new InstallResult(false, "無法讀取安裝腳本");
            }

            progress?.Report(new InstallProgress(10, "解析腳本批次..."));
            ct.ThrowIfCancellationRequested();

            var batches = SplitScriptIntoBatches(script);
            var totalBatches = batches.Count;

            progress?.Report(new InstallProgress(20, $"開始執行 {totalBatches} 個批次..."));

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            for (var i = 0; i < totalBatches; i++)
            {
                ct.ThrowIfCancellationRequested();

                var batch = batches[i];
                if (string.IsNullOrWhiteSpace(batch))
                    continue;

                var percentComplete = 20 + (int)((i + 1) * 70.0 / totalBatches);
                progress?.Report(new InstallProgress(percentComplete, $"執行批次 {i + 1}/{totalBatches}..."));

                await using var command = new SqlCommand(batch, connection)
                {
                    CommandTimeout = 300
                };
                await command.ExecuteNonQueryAsync(ct);
            }

            progress?.Report(new InstallProgress(95, "驗證安裝..."));
            ct.ThrowIfCancellationRequested();

            // 驗證安裝
            var validateSql = @"
SELECT COUNT(*)
FROM DBA.sys.tables t
JOIN DBA.sys.procedures p ON 1=1
JOIN DBA.sys.views v ON 1=1
WHERE t.name IN ('ServerHealthLog', 'MonitoringCategories')
    AND p.name = 'sp_MasterHealthCheck'
    AND v.name = 'vw_CurrentStatus';";

            await using var validateCommand = new SqlCommand(validateSql, connection)
            {
                CommandTimeout = 30
            };
            var validateResult = await validateCommand.ExecuteScalarAsync(ct);

            if (validateResult != null && (int)validateResult > 0)
            {
                progress?.Report(new InstallProgress(100, "安裝完成"));
                return new InstallResult(true);
            }

            return new InstallResult(false, "安裝驗證失敗");
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, "安裝已取消");
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"安裝失敗: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<UninstallResult> UninstallAsync(UninstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
        {
            return new UninstallResult(false, "無法取得連線字串");
        }

        try
        {
            progress?.Report(new InstallProgress(0, "準備移除..."));
            ct.ThrowIfCancellationRequested();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            // 步驟 1: 移除 SQL Agent 作業
            progress?.Report(new InstallProgress(10, "移除 SQL Agent 作業..."));
            ct.ThrowIfCancellationRequested();

            var removeJobsSql = @"
USE msdb;

IF EXISTS (SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'DBA - Server Health Monitoring')
    EXEC msdb.dbo.sp_delete_job @job_name = N'DBA - Server Health Monitoring';

IF EXISTS (SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'DBA - Cleanup Health Log')
    EXEC msdb.dbo.sp_delete_job @job_name = N'DBA - Cleanup Health Log';";

            await using (var cmd = new SqlCommand(removeJobsSql, connection) { CommandTimeout = 60 })
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 如果只移除作業，到此為止
            if (options.RemoveJobsOnly)
            {
                progress?.Report(new InstallProgress(100, "SQL Agent 作業已移除"));
                return new UninstallResult(true);
            }

            // 檢查 DBA 資料庫是否存在
            var dbExistsSql = "SELECT CASE WHEN EXISTS (SELECT name FROM sys.databases WHERE name = 'DBA') THEN 1 ELSE 0 END";
            await using (var cmd = new SqlCommand(dbExistsSql, connection) { CommandTimeout = 30 })
            {
                var exists = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
                if (exists == 0)
                {
                    progress?.Report(new InstallProgress(100, "移除完成（DBA 資料庫不存在）"));
                    return new UninstallResult(true);
                }
            }

            // 步驟 2: 移除視圖
            progress?.Report(new InstallProgress(30, "移除視圖..."));
            ct.ThrowIfCancellationRequested();

            var removeViewsSql = @"
USE DBA;

IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_RecentAlerts')
    DROP VIEW dbo.vw_RecentAlerts;

IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_CurrentStatus')
    DROP VIEW dbo.vw_CurrentStatus;

IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_TrendAnalysis')
    DROP VIEW dbo.vw_TrendAnalysis;";

            var viewBatches = SplitScriptIntoBatches(removeViewsSql);
            foreach (var batch in viewBatches)
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var cmd = new SqlCommand(batch, connection) { CommandTimeout = 60 };
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 步驟 3: 移除預存程序
            progress?.Report(new InstallProgress(50, "移除預存程序..."));
            ct.ThrowIfCancellationRequested();

            var removeProcsSql = @"
USE DBA;

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MasterHealthCheck')
    DROP PROCEDURE dbo.sp_MasterHealthCheck;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_LogHealthMetrics')
    DROP PROCEDURE dbo.sp_LogHealthMetrics;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorCPU')
    DROP PROCEDURE dbo.sp_MonitorCPU;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorBlocking')
    DROP PROCEDURE dbo.sp_MonitorBlocking;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorLongQueries')
    DROP PROCEDURE dbo.sp_MonitorLongQueries;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorBackups')
    DROP PROCEDURE dbo.sp_MonitorBackups;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorDeadlocks')
    DROP PROCEDURE dbo.sp_MonitorDeadlocks;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorConnections')
    DROP PROCEDURE dbo.sp_MonitorConnections;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorTempDB')
    DROP PROCEDURE dbo.sp_MonitorTempDB;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorTransactionLog')
    DROP PROCEDURE dbo.sp_MonitorTransactionLog;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorSQLAgentJobs')
    DROP PROCEDURE dbo.sp_MonitorSQLAgentJobs;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorPerformance')
    DROP PROCEDURE dbo.sp_MonitorPerformance;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_CleanupHealthLog')
    DROP PROCEDURE dbo.sp_CleanupHealthLog;
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_HealthReport')
    DROP PROCEDURE dbo.sp_HealthReport;";

            var procBatches = SplitScriptIntoBatches(removeProcsSql);
            foreach (var batch in procBatches)
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var cmd = new SqlCommand(batch, connection) { CommandTimeout = 60 };
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 如果保留歷史資料，到此為止
            if (options.KeepHistoryData)
            {
                progress?.Report(new InstallProgress(100, "移除完成（已保留歷史資料）"));
                return new UninstallResult(true);
            }

            // 步驟 4: 移除資料表
            progress?.Report(new InstallProgress(70, "移除資料表..."));
            ct.ThrowIfCancellationRequested();

            var removeTablesSql = @"
USE DBA;

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ServerHealthLog')
    DROP TABLE dbo.ServerHealthLog;

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'MonitoringCategories')
    DROP TABLE dbo.MonitoringCategories;";

            var tableBatches = SplitScriptIntoBatches(removeTablesSql);
            foreach (var batch in tableBatches)
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var cmd = new SqlCommand(batch, connection) { CommandTimeout = 60 };
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 步驟 5: 移除 DBA 資料庫（如果沒有其他物件）
            progress?.Report(new InstallProgress(90, "檢查是否可移除 DBA 資料庫..."));
            ct.ThrowIfCancellationRequested();

            var checkEmptySql = @"
SELECT
    (SELECT COUNT(*) FROM DBA.sys.tables) +
    (SELECT COUNT(*) FROM DBA.sys.procedures) +
    (SELECT COUNT(*) FROM DBA.sys.views) AS ObjectCount;";

            await using (var cmd = new SqlCommand(checkEmptySql, connection) { CommandTimeout = 30 })
            {
                var objectCount = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
                if (objectCount == 0)
                {
                    progress?.Report(new InstallProgress(95, "移除 DBA 資料庫..."));

                    var dropDbSql = @"
USE master;
ALTER DATABASE DBA SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE DBA;";

                    var dropBatches = SplitScriptIntoBatches(dropDbSql);
                    foreach (var batch in dropBatches)
                    {
                        if (string.IsNullOrWhiteSpace(batch)) continue;
                        await using var dropCmd = new SqlCommand(batch, connection) { CommandTimeout = 60 };
                        await dropCmd.ExecuteNonQueryAsync(ct);
                    }
                }
            }

            progress?.Report(new InstallProgress(100, "移除完成"));
            return new UninstallResult(true);
        }
        catch (OperationCanceledException)
        {
            return new UninstallResult(false, "移除已取消");
        }
        catch (Exception ex)
        {
            return new UninstallResult(false, $"移除失敗: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<string> GenerateExportSqlAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var script = await LoadEmbeddedScriptAsync("HealthMonitoringInstall.sql");
        if (string.IsNullOrEmpty(script))
        {
            throw new InvalidOperationException("無法讀取安裝腳本嵌入資源");
        }

        // 替換硬編碼值為 SQLCMD 變數
        var exportSql = script;

        // 資料庫名稱替換
        exportSql = exportSql.Replace("USE DBA;", "USE [$(DBADatabaseName)];");
        exportSql = exportSql.Replace("CREATE DATABASE DBA;", "CREATE DATABASE [$(DBADatabaseName)];");
        exportSql = exportSql.Replace("WHERE name = 'DBA'", "WHERE name = '$(DBADatabaseName)'");
        exportSql = exportSql.Replace("DELETE FROM DBA.dbo.ServerHealthLog", "DELETE FROM [$(DBADatabaseName)].dbo.ServerHealthLog");
        exportSql = exportSql.Replace("ALTER INDEX ALL ON DBA.dbo.ServerHealthLog", "ALTER INDEX ALL ON [$(DBADatabaseName)].dbo.ServerHealthLog");
        exportSql = exportSql.Replace("EXEC DBA.dbo.sp_MasterHealthCheck;", "EXEC [$(DBADatabaseName)].dbo.sp_MasterHealthCheck;");
        exportSql = exportSql.Replace("EXEC DBA.dbo.sp_CleanupHealthLog @RetentionDays = 30;", "EXEC [$(DBADatabaseName)].dbo.sp_CleanupHealthLog @RetentionDays = $(LogRetentionDays);");
        exportSql = exportSql.Replace("@command = N'EXEC DBA.dbo.sp_MasterHealthCheck;'", "@command = N'EXEC [$(DBADatabaseName)].dbo.sp_MasterHealthCheck;'");
        exportSql = exportSql.Replace("@command = N'EXEC DBA.dbo.sp_CleanupHealthLog @RetentionDays = 30;'", "@command = N'EXEC [$(DBADatabaseName)].dbo.sp_CleanupHealthLog @RetentionDays = $(LogRetentionDays);'");
        exportSql = exportSql.Replace("@database_name = N'DBA'", "@database_name = N'$(DBADatabaseName)'");

        // 驗證 SQL 中的 DBA 資料庫參考（如 DBA.sys.tables）
        exportSql = exportSql.Replace("FROM DBA.sys.", "FROM [$(DBADatabaseName)].sys.");

        // 長時間查詢閾值替換
        exportSql = exportSql.Replace("@ThresholdSeconds INT = 300", "@ThresholdSeconds INT = $(LongQueryThresholdSeconds)");
        exportSql = exportSql.Replace("EXEC sp_MonitorLongQueries @ThresholdSeconds = 300;", "EXEC sp_MonitorLongQueries @ThresholdSeconds = $(LongQueryThresholdSeconds);");

        // 清理程序保留天數替換
        exportSql = exportSql.Replace("@RetentionDays INT = 30", "@RetentionDays INT = $(LogRetentionDays)");

        // Job 名稱替換
        exportSql = exportSql.Replace("N'DBA - Server Health Monitoring'", "N'$(HealthCheckJobName)'");
        exportSql = exportSql.Replace("N'DBA - Cleanup Health Log'", "N'$(CleanupJobName)'");

        // Job 描述替換
        exportSql = exportSql.Replace("N'[Specurai] 每日清理超過30天的記錄'", "N'[Specurai] 每日清理超過$(LogRetentionDays)天的記錄'");

        // 排程設定替換
        exportSql = exportSql.Replace("@active_start_time = 20000;", "@active_start_time = $(CleanupTime);");

        // 重試設定替換
        exportSql = exportSql.Replace("@retry_attempts = 3,", "@retry_attempts = $(RetryAttempts),");
        exportSql = exportSql.Replace("@retry_interval = 5;", "@retry_interval = $(RetryInterval);");

        // 組合最終腳本：標題 + 變數宣告區 + 修改後的安裝腳本
        var header = $"""
            -- ============================================================
            -- Specurai 健康監控系統安裝腳本
            -- 產生時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            -- 說明: 此腳本由 Specurai 自動產生
            --       請在 SSMS 啟用 SQLCMD 模式後執行，或修改下方變數
            -- ============================================================

            -- ============================================================
            -- SQLCMD 變數設定區（請依需求修改）
            -- 在 SSMS 中：查詢 → SQLCMD 模式，即可使用 :setvar
            -- ============================================================

            -- 健康監控資料庫名稱（用於儲存監控記錄）
            :setvar DBADatabaseName "DBA"

            -- 記錄保留天數（超過此天數的舊記錄將自動刪除）
            :setvar LogRetentionDays "30"

            -- 長時間查詢閾值（秒，超過此時間的查詢將被記錄為異常）
            :setvar LongQueryThresholdSeconds "300"

            -- 監控 Job 名稱 - 每小時健康檢查
            :setvar HealthCheckJobName "DBA - Server Health Monitoring"

            -- 監控 Job 名稱 - 每日記錄清理
            :setvar CleanupJobName "DBA - Cleanup Health Log"

            -- 每日清理排程時間（HHMMSS 格式，20000 = 02:00:00）
            :setvar CleanupTime "20000"

            -- 監控 Job 失敗時重試次數
            :setvar RetryAttempts "3"

            -- 監控 Job 失敗時重試間隔（分鐘）
            :setvar RetryInterval "5"

            """;

        // 移除原始腳本開頭的註解標題（已由新標題取代）
        var scriptStartIndex = exportSql.IndexOf("SET NOCOUNT ON;", StringComparison.Ordinal);
        if (scriptStartIndex >= 0)
        {
            exportSql = exportSql[scriptStartIndex..];
        }

        return header + exportSql;
    }

    private static async Task<string?> LoadEmbeddedScriptAsync(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(resourceName))
            return null;

        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static List<string> SplitScriptIntoBatches(string script)
    {
        // 使用正規表達式分割 GO 語句
        var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();

        return batches;
    }
}
