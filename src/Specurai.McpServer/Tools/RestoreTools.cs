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
    [McpServerTool, TargetConnection(AcceptsDatabase = false), Description("從備份檔案還原資料庫（⚠️ 破壞性操作：overwrite 模式會覆蓋現有資料庫，無法復原；預設僅回摘要，需 confirm:true 才實際執行）")]
    public static async Task<string> RestoreRun(
        IConnectionManager connectionManager,
        IBackupService backupService,
        [Description("備份檔案路徑（SQL Server 伺服器端路徑）")] string path,
        [Description("還原模式：overwrite（覆蓋現有）/ new（建立新資料庫，預設 overwrite）")] string mode = "overwrite",
        [Description("目標資料庫名稱（mode=new 時必填）")] string? target = null,
        [Description("資料檔路徑（mode=new 時可指定，SQL Server 伺服器端路徑）")] string? dataPath = null,
        [Description("日誌檔路徑（mode=new 時可指定，SQL Server 伺服器端路徑）")] string? logPath = null,
        [Description(ConnectionTarget.ConnectionDescription)] string? connection = null,
        IDatabaseRecoveryModelService? recoveryModelService = null,
        CancellationToken cancellationToken = default,
        ModelContextProtocol.Server.McpServer? server = null,
        [Description("是否實際執行（預設 false 僅回摘要）")] bool confirm = false)
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

            // overwrite 實際覆蓋的是備份檔記錄的來源庫，摘要與確認名稱都以它為準
            string targetName;
            if (restoreMode == RestoreMode.OverwriteExisting)
            {
                targetName = (await backupService.GetBackupFileInfoAsync(connStr, path)).DatabaseName;
                if (!string.IsNullOrWhiteSpace(target) && !target.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                    return $"overwrite 模式會覆蓋備份來源庫 {targetName}，與指定的 target {target} 不符，未執行。要還原成另一個庫請用 mode=new。";
            }
            else
            {
                targetName = target!;
                // 沒有 REPLACE 的 RESTORE 在部分情況仍可能覆蓋同名庫，目標已存在就不做
                if (recoveryModelService == null)
                    return $"無法確認目標資料庫 {targetName} 是否已存在，未執行。";
                if ((await recoveryModelService.GetAllAsync(cancellationToken))
                        .Any(d => d.DatabaseName.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
                    return $"目標資料庫 {targetName} 已存在，new 模式不覆蓋既有資料庫，未執行。";
            }
            var overwriteNote = restoreMode == RestoreMode.OverwriteExisting
                ? "；overwrite 會覆蓋現有資料庫，無法復原" : "";
            var summary = $"將從 {HumanConfirm.Quote(path)} 還原到 {HumanConfirm.Quote(targetName)}（模式 {restoreMode}{overwriteNote}）。";
            if (!confirm)
                return $"{summary}加 confirm:true 執行。";

            // 覆蓋無法復原，需輸入目標名稱；建新庫只需接受
            var refusal = await HumanConfirm.RequireAsync(server, summary,
                restoreMode == RestoreMode.OverwriteExisting ? targetName : null, profile, cancellationToken);
            if (refusal != null)
                return refusal;

            await backupService.RestoreDatabaseAsync(connStr, path, options);
            return $"還原完成（模式：{restoreMode}，目標：{targetName}）。";
        }
        catch (Exception ex)
        {
            return $"還原失敗：{ex.Message}";
        }
    }
}
