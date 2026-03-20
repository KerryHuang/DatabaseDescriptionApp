using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// SQL Agent Job 管理 MCP 工具
/// </summary>
[McpServerToolType]
public static class AgentJobTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("列出 Specurai 管理的 SQL Agent Job 清單，包含狀態、上次執行結果、排程資訊")]
    public static async Task<string> ListAgentJobs(IAgentJobService agentJobService)
    {
        try
        {
            var jobs = await agentJobService.GetJobsAsync();
            if (jobs.Count == 0)
                return "目前沒有 Specurai 管理的 Agent Job。";
            return JsonSerializer.Serialize(jobs, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得 Agent Job 清單失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("列出未被 Specurai 管理的 SQL Agent Job，可透過 ImportAgentJob 匯入管理")]
    public static async Task<string> ListNonSpecuraiJobs(IAgentJobService agentJobService)
    {
        try
        {
            var jobs = await agentJobService.GetNonSpecuraiJobsAsync();
            if (jobs.Count == 0)
                return "沒有未管理的 Agent Job。";
            return JsonSerializer.Serialize(jobs, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得 Job 清單失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("取得指定 Agent Job 的執行歷史紀錄")]
    public static async Task<string> GetAgentJobHistory(
        IAgentJobService agentJobService,
        [Description("Job ID")] string jobId,
        [Description("最大紀錄數（預設 20）")] int maxRecords = 20)
    {
        try
        {
            var history = await agentJobService.GetJobHistoryAsync(Guid.Parse(jobId), maxRecords);
            if (history.Count == 0)
                return "此 Job 沒有執行歷史紀錄。";
            return JsonSerializer.Serialize(history, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得 Job 歷史紀錄失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("啟用或停用指定的 Agent Job（⚠️ 寫入操作）")]
    public static async Task<string> SetAgentJobEnabled(
        IAgentJobService agentJobService,
        [Description("Job ID")] string jobId,
        [Description("true=啟用, false=停用")] bool enabled)
    {
        try
        {
            await agentJobService.SetJobEnabledAsync(Guid.Parse(jobId), enabled);
            return $"已{(enabled ? "啟用" : "停用")} Agent Job。";
        }
        catch (Exception ex)
        {
            return $"操作失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("立即執行指定的 Agent Job（⚠️ 寫入操作）")]
    public static async Task<string> StartAgentJob(
        IAgentJobService agentJobService,
        [Description("Job ID")] string jobId)
    {
        try
        {
            await agentJobService.StartJobAsync(Guid.Parse(jobId));
            return "已觸發 Agent Job 執行。";
        }
        catch (Exception ex)
        {
            return $"啟動 Job 失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("刪除指定的 Agent Job（⚠️ 破壞性操作，無法復原）")]
    public static async Task<string> DeleteAgentJob(
        IAgentJobService agentJobService,
        [Description("Job ID")] string jobId)
    {
        try
        {
            await agentJobService.DeleteJobAsync(Guid.Parse(jobId));
            return "已刪除 Agent Job。";
        }
        catch (Exception ex)
        {
            return $"刪除 Job 失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("更新 Agent Job 的排程設定（⚠️ 寫入操作）")]
    public static async Task<string> UpdateAgentJobSchedule(
        IAgentJobService agentJobService,
        [Description("Job ID")] string jobId,
        [Description("頻率類型（1=一次, 4=每天, 8=每週, 16=每月）")] int freqType,
        [Description("頻率間隔")] int freqInterval,
        [Description("執行時間（HHMMSS 格式，如 23000 = 02:30:00）")] int activeStartTime)
    {
        try
        {
            await agentJobService.UpdateScheduleAsync(Guid.Parse(jobId), freqType, freqInterval, activeStartTime);
            return "已更新 Agent Job 排程。";
        }
        catch (Exception ex)
        {
            return $"更新排程失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("將指定的 Job 匯入 Specurai 管理（⚠️ 寫入操作，加上 [Specurai] 標記）")]
    public static async Task<string> ImportAgentJob(
        IAgentJobService agentJobService,
        [Description("Job ID")] string jobId)
    {
        try
        {
            await agentJobService.ImportJobAsync(Guid.Parse(jobId));
            return "已匯入 Agent Job 至 Specurai 管理。";
        }
        catch (Exception ex)
        {
            return $"匯入 Job 失敗：{ex.Message}";
        }
    }
}
