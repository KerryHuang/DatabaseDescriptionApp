using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 健康監控安裝/移除 MCP 工具
/// </summary>
[McpServerToolType]
public static class HealthInstallerTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("在目前資料庫中安裝健康監控系統（⚠️ 寫入操作，會建立資料表、預存程序和排程）")]
    public static async Task<string> InstallHealthMonitoring(
        IHealthMonitoringService healthService)
    {
        try
        {
            var result = await healthService.InstallAsync();
            return result.Success
                ? "健康監控系統安裝成功。"
                : $"安裝失敗：{result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            return $"安裝健康監控系統失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("移除健康監控系統（⚠️ 破壞性操作；預設僅回摘要，需 confirm:true 才實際執行）")]
    public static async Task<string> UninstallHealthMonitoring(
        IHealthMonitoringService healthService,
        [Description("是否保留歷史資料（預設 false）")] bool keepHistoryData = false,
        [Description("僅移除排程 Job（預設 false）")] bool removeJobsOnly = false,
        [Description("是否實際執行（預設 false 僅回摘要）")] bool confirm = false)
    {
        try
        {
            if (!confirm)
            {
                var scope = removeJobsOnly
                    ? "僅移除排程 Job"
                    : (keepHistoryData ? "移除健康監控系統但保留歷史資料" : "移除健康監控系統（含歷史資料）");
                return $"將{scope}。加 confirm:true 執行。";
            }

            var options = new UninstallOptions(keepHistoryData, removeJobsOnly);
            var result = await healthService.UninstallAsync(options);
            return result.Success
                ? "健康監控系統已移除。"
                : $"移除失敗：{result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            return $"移除健康監控系統失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("產生健康監控安裝的完整 SQL 腳本，可在其他資料庫手動執行")]
    public static async Task<string> ExportHealthMonitoringSql(
        IHealthMonitoringService healthService)
    {
        try
        {
            var sql = await healthService.GenerateExportSqlAsync();
            return sql;
        }
        catch (Exception ex)
        {
            return $"產生 SQL 腳本失敗：{ex.Message}";
        }
    }
}
