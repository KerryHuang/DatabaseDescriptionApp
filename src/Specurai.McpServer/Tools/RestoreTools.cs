using System.ComponentModel;
using ModelContextProtocol.Server;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 資料庫還原 MCP 工具
/// </summary>
[McpServerToolType]
public static class RestoreTools
{
    [McpServerTool, Description("從備份檔案還原資料庫（⚠️ 破壞性操作：overwrite 模式會覆蓋現有資料庫，無法復原）")]
    public static async Task<string> RestoreRun(
        IConnectionManager connectionManager,
        IBackupService backupService,
        [Description("備份檔案路徑（SQL Server 伺服器端路徑）")] string path,
        [Description("還原模式：overwrite（覆蓋現有）/ new（建立新資料庫，預設 overwrite）")] string mode = "overwrite",
        [Description("目標資料庫名稱（mode=new 時必填）")] string? target = null,
        [Description("資料檔路徑（mode=new 時可指定，SQL Server 伺服器端路徑）")] string? dataPath = null,
        [Description("日誌檔路徑（mode=new 時可指定，SQL Server 伺服器端路徑）")] string? logPath = null)
    {
        try
        {
            var profile = connectionManager.GetCurrentProfile();
            if (profile == null)
                return "尚未選擇連線。請先以 switch_connection 切換連線。";

            var restoreMode = mode.ToLowerInvariant() switch
            {
                "new" or "create" => RestoreMode.CreateNew,
                _ => RestoreMode.OverwriteExisting
            };

            if (restoreMode == RestoreMode.CreateNew && string.IsNullOrWhiteSpace(target))
                return "mode=new 時必須指定 target。";

            var options = new RestoreOptions
            {
                Mode = restoreMode,
                TargetDatabaseName = target,
                DataFilePath = dataPath,
                LogFilePath = logPath,
                WithReplace = restoreMode == RestoreMode.OverwriteExisting,
                WithRecovery = true,
                ShowProgress = false
            };

            // 還原需連到 master 資料庫
            var masterProfile = new ConnectionProfile
            {
                Name = profile.Name,
                Server = profile.Server,
                Database = "master",
                AuthType = profile.AuthType,
                Username = profile.Username,
                Password = profile.Password
            };
            var connStr = connectionManager.BuildConnectionString(masterProfile);

            await backupService.RestoreDatabaseAsync(connStr, path, options);
            return $"還原完成（模式：{restoreMode}，目標：{target ?? profile.Database}）。";
        }
        catch (Exception ex)
        {
            return $"還原失敗：{ex.Message}";
        }
    }
}
