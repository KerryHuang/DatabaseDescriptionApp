using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 資料庫備份 MCP 工具
/// </summary>
[McpServerToolType]
public static class BackupTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("備份目前連線的資料庫到指定路徑（⚠️ 寫入操作，會在伺服器端產生 .bak 檔）")]
    public static async Task<string> BackupRun(
        IConnectionManager connectionManager,
        IBackupService backupService,
        [Description("備份檔案路徑（.bak），為 SQL Server 伺服器端路徑")] string path,
        [Description("備份類型：full / diff / log（預設 full）")] string type = "full",
        [Description("備份描述（可選）")] string? description = null,
        [Description("備份後是否略過驗證（預設 false）")] bool noVerify = false)
    {
        try
        {
            var profile = connectionManager.GetCurrentProfile();
            if (profile == null)
                return "尚未選擇連線。請先以 switch_connection 切換連線。";

            var backupType = type.ToLowerInvariant() switch
            {
                "full" => BackupType.Full,
                "diff" or "differential" => BackupType.Differential,
                "log" or "trn" => BackupType.TransactionLog,
                _ => BackupType.Full
            };

            var connStr = connectionManager.BuildConnectionString(profile);
            var info = await backupService.BackupDatabaseAsync(connStr, profile.Id, profile.Name, path, backupType, description);

            if (!noVerify)
            {
                var verify = await backupService.VerifyBackupAsync(connStr, path);
                if (!verify.IsValid)
                    return $"備份已建立但驗證失敗：{verify.ErrorMessage}";
            }

            return JsonSerializer.Serialize(info, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"備份失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("驗證備份檔案是否有效")]
    public static async Task<string> BackupVerify(
        IConnectionManager connectionManager,
        IBackupService backupService,
        [Description("備份檔案路徑")] string path)
    {
        try
        {
            var profile = connectionManager.GetCurrentProfile();
            if (profile == null)
                return "尚未選擇連線。請先以 switch_connection 切換連線。";

            var connStr = connectionManager.BuildConnectionString(profile);
            var result = await backupService.VerifyBackupAsync(connStr, path);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"驗證備份失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("查看備份檔案的詳細資訊（資料庫、伺服器、時間、大小等）")]
    public static async Task<string> BackupInfo(
        IConnectionManager connectionManager,
        IBackupService backupService,
        [Description("備份檔案路徑")] string path)
    {
        try
        {
            var profile = connectionManager.GetCurrentProfile();
            if (profile == null)
                return "尚未選擇連線。請先以 switch_connection 切換連線。";

            var connStr = connectionManager.BuildConnectionString(profile);
            var info = await backupService.GetBackupFileInfoAsync(connStr, path);
            return JsonSerializer.Serialize(info, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得備份資訊失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("顯示備份歷史記錄")]
    public static string BackupHistory(IBackupService backupService)
    {
        try
        {
            var history = backupService.GetBackupHistory();
            var backups = history.Backups.OrderByDescending(b => b.BackupTime).ToList();
            if (backups.Count == 0)
                return "沒有備份歷史記錄。";
            return JsonSerializer.Serialize(backups, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得備份歷史失敗：{ex.Message}";
        }
    }
}
