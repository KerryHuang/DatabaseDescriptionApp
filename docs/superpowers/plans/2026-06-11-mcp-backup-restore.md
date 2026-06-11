# B4 · MCP 備份還原工具（BackupTools / RestoreTools）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 為 `Specurai.McpServer` 新增 `BackupTools`（backup_run / verify / info / history）與 `RestoreTools`（restore_run），鏡像 CLI `BackupCommand`/`RestoreCommand` 對 `IBackupService` 的編排，使 MCP 與 CLI 在備份還原上對齊（依決議全部暴露，含破壞性操作）。

**Architecture:** 純展示層接線。MCP 工具為帶 `[McpServerToolType]`/`[McpServerTool]` 的靜態類別，由 `.WithToolsFromAssembly()` 自動探索，**不需改 `Program.cs`**。服務 `IBackupService`、`IConnectionManager` 已由 `AddSpecuraiCore()` 註冊。連線解析以 `IConnectionManager.GetCurrentProfile()` + `BuildConnectionString()` 取代 CLI 的 `ConnectionResolver`。沿用既有 MCP 工具回傳字串、以 try/catch 包裝回傳友善錯誤訊息的慣例。

**Tech Stack:** .NET 8、ModelContextProtocol SDK、System.Text.Json。

---

## 測試與驗證策略（重要）

- 本案沿用**既有 MCP 工具「薄 DI 包裝、無單元測試」慣例**（專案目前無 McpServer 測試專案，所有既有 MCP 工具與所鏡像的 CLI backup/restore 命令皆無單元測試）。工具僅編排已存在且已於 Infrastructure 層實作的 `IBackupService`。
- 驗證方式：`dotnet build` 成功 + 程式碼審查 + **唯讀/安全**煙霧測試（`backup_history`，純讀本機歷史檔）。
- **破壞性操作 `backup_run`（寫備份檔）與 `restore_run`（覆蓋資料庫）不對真實資料庫煙霧測試**，以避免副作用；其正確性以「忠實鏡像已審查的 CLI 編排邏輯」+ code review 保證。

---

## File Structure

- Create: `src/Specurai.McpServer/Tools/BackupTools.cs` — backup_run / backup_verify / backup_info / backup_history。
- Create: `src/Specurai.McpServer/Tools/RestoreTools.cs` — restore_run。

鏡像來源：`src/Specurai.Cli/Commands/BackupCommand.cs`、`RestoreCommand.cs`。

關鍵既有型別（已驗證）：
- `IConnectionManager`：`GetCurrentProfile()→ConnectionProfile?`、`BuildConnectionString(ConnectionProfile)→string`。
- `IBackupService.BackupDatabaseAsync(connStr, Guid connectionId, string connectionName, string backupPath, BackupType, string? description, IProgress?, CancellationToken)→Task<BackupInfo>`。
- `IBackupService.VerifyBackupAsync(connStr, backupPath, ct)→Task<BackupVerifyResult>`；`GetBackupFileInfoAsync(connStr, backupPath, ct)→Task<BackupFileInfo>`；`GetBackupHistory()→BackupHistory`（`BackupHistory.Backups : List<BackupInfo>`）。
- `IBackupService.RestoreDatabaseAsync(connStr, backupPath, RestoreOptions, IProgress?, CancellationToken)→Task`。
- `BackupType`（`Specurai.Domain.Enums`）：Full / Differential / TransactionLog。
- `RestoreMode`（`Specurai.Domain.Enums`）：OverwriteExisting / CreateNew。
- `RestoreOptions`（`Specurai.Domain.Entities`）：`Mode, TargetDatabaseName, DataFilePath, LogFilePath, WithReplace, WithRecovery, ShowProgress`。
- `ConnectionProfile`（`Specurai.Domain.Entities`）：`Name, Server, Database, AuthType, Username, Password`（皆 settable，`Name/Server/Database` 為 required）。

---

## Task 1: `BackupTools`

**Files:** Create `src/Specurai.McpServer/Tools/BackupTools.cs`

- [ ] **Step 1: 建立工具類別**

```csharp
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
```

- [ ] **Step 2: 建置 McpServer**

Run: `dotnet build src/Specurai.McpServer/Specurai.McpServer.csproj`
Expected: Build succeeded，0 error。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.McpServer/Tools/BackupTools.cs
git commit -m "feat(mcp): BackupTools 對齊 CLI backup（run/verify/info/history）"
```

---

## Task 2: `RestoreTools`

**Files:** Create `src/Specurai.McpServer/Tools/RestoreTools.cs`

- [ ] **Step 1: 建立工具類別**

```csharp
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
        [Description("備份檔案路徑")] string path,
        [Description("還原模式：overwrite（覆蓋現有）/ new（建立新資料庫，預設 overwrite）")] string mode = "overwrite",
        [Description("目標資料庫名稱（mode=new 時必填）")] string? target = null,
        [Description("資料檔路徑（mode=new 時可指定）")] string? dataPath = null,
        [Description("日誌檔路徑（mode=new 時可指定）")] string? logPath = null)
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
```

- [ ] **Step 2: 建置 McpServer**

Run: `dotnet build src/Specurai.McpServer/Specurai.McpServer.csproj`
Expected: Build succeeded，0 error。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.McpServer/Tools/RestoreTools.cs
git commit -m "feat(mcp): RestoreTools 對齊 CLI restore（restore_run，破壞性全暴露）"
```

---

## Task 3: 驗證與審查

- [ ] **Step 1: McpServer 建置綠燈**

Run: `dotnet build src/Specurai.McpServer/Specurai.McpServer.csproj`
Expected: Build succeeded。

- [ ] **Step 2: 程式碼審查**

使用 `superpowers:requesting-code-review` 技能審查 `BackupTools.cs`、`RestoreTools.cs`，確認與 CLI 鏡像來源行為一致、連線解析正確、破壞性工具的描述有清楚標註。

- [ ] **Step 3: 安全煙霧測試（可選，需 republish + 關閉行程）**

> 注意：MCP 變更需重新 publish `Specurai.McpServer.exe`（須先關閉執行中的 MCP 行程）才會在 client 生效。可於 B5 一併完成後再 republish。若要單獨驗證，僅執行唯讀的 `backup_history`（安全）；勿對真實資料庫執行 `backup_run`/`restore_run`。

---

## Self-Review 紀錄

- **Spec 覆蓋**：B4 範圍（BackupTools run/verify/info/history、RestoreTools run）皆有對應 Task。✅
- **Placeholder 掃描**：無 TBD/TODO；每個程式碼步驟均含完整程式碼。✅
- **型別一致性**：`IBackupService` 各方法簽章、`IConnectionManager.GetCurrentProfile/BuildConnectionString`、`BackupType`/`RestoreMode`/`RestoreOptions`/`ConnectionProfile` 皆與既有定義相符；type/mode 字串映射與 CLI 鏡像來源逐一致。✅
- **鏡像一致性**：backup_run 的 verify 流程、restore_run 的 master 連線與 RestoreOptions 建構，與 `BackupCommand.cs`/`RestoreCommand.cs` 一致。CLI 的互動確認（AnsiConsole.Confirm）在 MCP 不適用，依「全部暴露」決議省略，並於工具描述以 ⚠️ 標註破壞性。
- **刻意取捨**：沿用既有 MCP 工具「薄包裝無單元測試」慣例；破壞性操作不對真實資料庫煙霧測試。
