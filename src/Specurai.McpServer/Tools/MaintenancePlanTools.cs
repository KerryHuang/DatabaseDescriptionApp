using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Enums;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 維護計劃 MCP 工具
/// </summary>
[McpServerToolType]
public static class MaintenancePlanTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("檢查執行維護計劃的前置條件（SQL Agent 是否執行、權限等）")]
    public static async Task<string> CheckMaintenancePrerequisites(
        IMaintenancePlanService maintenancePlanService)
    {
        try
        {
            var (isReady, errorMessage) = await maintenancePlanService.CheckPrerequisitesAsync();
            return isReady
                ? "前置條件檢查通過，可以執行維護計劃。"
                : $"前置條件未通過：{errorMessage}";
        }
        catch (Exception ex)
        {
            return $"檢查前置條件失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("檢查維護計劃各步驟的目前狀態")]
    public static async Task<string> CheckMaintenanceSteps(
        IMaintenancePlanService maintenancePlanService,
        [Description("資料庫名稱")] string databaseName,
        [Description("備份路徑")] string backupPath,
        [Description("還原路徑")] string restorePath,
        [Description("測試資料庫名稱")] string testDatabaseName,
        [Description("登入帳號")] string loginName,
        [Description("登入密碼")] string loginPassword,
        [Description("備份時間（HHMMSS 格式，如 230000）")] int backupTime = 230000,
        [Description("已忽略（還原併入每日備份 Job 的第二步，於備份時間執行；保留以相容舊呼叫）")] int restoreTime = 10000)
    {
        try
        {
            var config = BuildConfig(databaseName, backupPath, restorePath,
                testDatabaseName, loginName, loginPassword, backupTime, restoreTime);
            var results = await maintenancePlanService.CheckStepsAsync(config);
            return JsonSerializer.Serialize(results, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"檢查步驟失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("產生維護計劃的預覽 SQL 腳本（不會實際執行）")]
    public static async Task<string> GenerateMaintenancePlanSql(
        IMaintenancePlanService maintenancePlanService,
        [Description("資料庫名稱")] string databaseName,
        [Description("備份路徑")] string backupPath,
        [Description("還原路徑")] string restorePath,
        [Description("測試資料庫名稱")] string testDatabaseName,
        [Description("登入帳號")] string loginName,
        [Description("登入密碼")] string loginPassword,
        [Description("備份時間（HHMMSS 格式）")] int backupTime = 230000,
        [Description("已忽略（還原併入每日備份 Job 的第二步，於備份時間執行；保留以相容舊呼叫）")] int restoreTime = 10000)
    {
        try
        {
            var config = BuildConfig(databaseName, backupPath, restorePath,
                testDatabaseName, loginName, loginPassword, backupTime, restoreTime);
            var checkResults = await maintenancePlanService.CheckStepsAsync(config);
            var sql = await maintenancePlanService.GeneratePreviewSqlAsync(config, checkResults);
            return sql;
        }
        catch (Exception ex)
        {
            return $"產生 SQL 腳本失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("執行維護計劃（⚠️ 破壞性操作，會修改資料庫設定和建立排程；預設僅回摘要，需 confirm:true 才實際執行）")]
    public static async Task<string> ExecuteMaintenancePlan(
        IMaintenancePlanService maintenancePlanService,
        [Description("資料庫名稱")] string databaseName,
        [Description("備份路徑")] string backupPath,
        [Description("還原路徑")] string restorePath,
        [Description("測試資料庫名稱")] string testDatabaseName,
        [Description("登入帳號")] string loginName,
        [Description("登入密碼")] string loginPassword,
        [Description("備份時間（HHMMSS 格式）")] int backupTime = 230000,
        [Description("已忽略（還原併入每日備份 Job 的第二步，於備份時間執行；保留以相容舊呼叫）")] int restoreTime = 10000,
        [Description("是否實際執行（預設 false 僅回摘要）")] bool confirm = false)
    {
        try
        {
            if (!confirm)
                return $"將對資料庫 [{databaseName}] 執行維護計劃（建立每日備份排程（含還原步驟，預設停用）、修改資料庫設定）。加 confirm:true 執行。";

            var config = BuildConfig(databaseName, backupPath, restorePath,
                testDatabaseName, loginName, loginPassword, backupTime, restoreTime);
            var checkResults = await maintenancePlanService.CheckStepsAsync(config);
            await maintenancePlanService.ExecutePlanAsync(config, checkResults);
            return "維護計劃已成功執行。";
        }
        catch (Exception ex)
        {
            return $"執行維護計劃失敗：{ex.Message}";
        }
    }

    private static MaintenancePlanConfig BuildConfig(
        string databaseName, string backupPath, string restorePath,
        string testDatabaseName, string loginName, string loginPassword,
        int backupTime, int restoreTime)
    {
        return new MaintenancePlanConfig
        {
            DatabaseName = databaseName,
            BackupPath = backupPath,
            RestorePath = restorePath,
            TestDatabaseName = testDatabaseName,
            LoginName = loginName,
            LoginPassword = loginPassword,
            BackupTime = backupTime,
            RestoreTime = restoreTime,
            SelectedSteps = Enum.GetValues<MaintenancePlanStep>().ToList()
        };
    }
}
