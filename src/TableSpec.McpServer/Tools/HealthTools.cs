using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TableSpec.Application.Services;

namespace TableSpec.McpServer.Tools;

/// <summary>
/// 健康監控 MCP 工具
/// </summary>
[McpServerToolType]
public static class HealthTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 取得健康監控安裝狀態
    /// </summary>
    [McpServerTool, Description("檢查健康監控系統是否已安裝在目前的資料庫中")]
    public static async Task<string> GetHealthInstallStatus(
        IHealthMonitoringService healthService)
    {
        try
        {
            var status = await healthService.GetInstallStatusAsync();
            return JsonSerializer.Serialize(status, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得健康監控安裝狀態失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得健康狀態摘要
    /// </summary>
    [McpServerTool, Description("取得資料庫健康狀態摘要，包含各檢查項目的狀態")]
    public static async Task<string> GetHealthStatus(
        IHealthMonitoringService healthService)
    {
        try
        {
            var summary = await healthService.GetStatusSummaryAsync();

            if (summary.Count == 0)
                return "沒有健康狀態資料。請先確認健康監控系統已安裝。";

            return JsonSerializer.Serialize(summary, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得健康狀態摘要失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得目前健康指標
    /// </summary>
    [McpServerTool, Description("取得目前的資料庫健康指標數值")]
    public static async Task<string> GetHealthMetrics(
        IHealthMonitoringService healthService)
    {
        try
        {
            var metrics = await healthService.GetCurrentMetricsAsync();

            if (metrics.Count == 0)
                return "沒有健康指標資料。請先確認健康監控系統已安裝。";

            return JsonSerializer.Serialize(metrics, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得健康指標失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得最近告警
    /// </summary>
    [McpServerTool, Description("取得最近的健康監控告警記錄")]
    public static async Task<string> GetHealthAlerts(
        IHealthMonitoringService healthService,
        [Description("查詢天數（預設 7 天）")] int days = 7)
    {
        try
        {
            var alerts = await healthService.GetRecentAlertsAsync(days);

            if (alerts.Count == 0)
                return $"最近 {days} 天沒有告警記錄。";

            return JsonSerializer.Serialize(alerts, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得告警記錄失敗：{ex.Message}";
        }
    }
}
