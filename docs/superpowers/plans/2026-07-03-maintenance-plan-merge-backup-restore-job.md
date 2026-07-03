# 維護計劃備份與還原合併為單一 Job 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 維護計劃的每日備份與還原合併為單一 SQL Agent Job（`{DB}_{RecoveryModel}Backup`）、單一每日排程、雙步驟（Step 1 備份、Step 2 還原），還原步驟建立但預設不執行；移除獨立還原 Job 與 `CreateRestoreJob` 步驟。

**Architecture:** 變更集中在共用 `MaintenancePlanSqlGenerator`（兩條產生路徑），連動 Domain enum、Application 服務/模型、Desktop VM/View、CLI/MCP 參數。分四個任務，每個任務結束時整個方案可建置且測試綠燈；`CreateRestoreJob` enum 值保留到最後一個任務才移除（在所有引用清除後）。

**Tech Stack:** .NET 8、Clean Architecture、Dapper、Avalonia 11、xUnit + NSubstitute + FluentAssertions。

## Global Constraints

- UI 文字、註解、Commit 訊息一律**繁體中文**。
- Clean Architecture 分層；ViewModel 不含查詢邏輯。
- 每個 ViewModel 保留無參數設計時建構函式與 DI 建構函式。
- 檔案 UTF-8 無 BOM。
- 測試命名 `[方法]_[條件]_[預期]`（繁體中文）。
- **排程結構**：單一 Job `{DB}_{RecoveryModel}Backup`、單一 `sp_add_jobschedule`（`@active_start_time = @BackupTime` / `config.BackupTime`）、兩個 `sp_add_jobstep`（Step 1 `Full Backup...`、Step 2 `Restore Full to...`）。
- Step 1 維持 `@on_success_action = 1`、`@on_fail_action = 2`（使 Step 2 預設不觸發）。
- Step 2 還原用 `@on_success_action = 1`、`@on_fail_action = 2`。
- 產出**不得**再含獨立 `{DB}_FullRestore` Job，或以 `RestoreTime` 為 `@active_start_time` 的排程。
- 備份 Job 需含註解說明：欲啟用還原，將 Step 1 的成功時動作改為「移至下一步」。
- `MaintenancePlanConfig.RestoreTime` 欄位保留（no-op）；`TestDatabaseName`、`RestorePath` 仍供還原 Step 使用。

---

### Task 1: Infrastructure 產生器合併還原步驟並移除獨立還原 Job

**Files:**
- Modify: `src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs`
- Test: `tests/Specurai.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs`

**Interfaces:**
- Consumes: `MaintenancePlanConfig`（`DatabaseName`、`RecoveryModel`、`BackupPath`、`RestorePath`、`TestDatabaseName`、`BackupTime`、`RetentionDays`）。
- Produces: 合併後的備份 Job SQL（單一 Job 雙步驟）。`MaintenancePlanStep.CreateRestoreJob` enum 值本任務**不移除**（保留以維持全案編譯），僅不再由產生器產生獨立還原 Job。

- [ ] **Step 1: 改寫測試（RED）**

於 `MaintenancePlanSqlGeneratorTests.cs`：
- 找出現有針對 `GenerateCreateRestoreJob` / 獨立還原 Job / 以 `RestoreTime` 排程的測試，改寫或移除。
- 為 `GenerateCreateBackupJob` 新增/更新測試，斷言合併結果。範例（依現有測試風格調整 `CreateConfig` helper 名稱）：

```csharp
[Fact]
public void GenerateCreateBackupJob_合併還原_應產生單一Job雙步驟且還原預設不觸發()
{
    // Arrange
    var config = CreateConfig() with
    {
        DatabaseName = "WayDoSoft01",
        RecoveryModel = "SIMPLE",
        BackupPath = @"D:\SQLBackup\",
        RestorePath = @"D:\sql_data\",
        TestDatabaseName = "WayDoSoft01",
        BackupTime = 20000
    };

    // Act
    var sql = _generator.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, config, null);

    // Assert：單一 Job
    sql.Should().Contain("WayDoSoft01_SIMPLEBackup");
    sql.Should().NotContain("_FullRestore");
    // 兩個步驟
    sql.Should().Contain("Full Backup");
    sql.Should().Contain("Restore Full to");
    Regex.Matches(sql, "sp_add_jobstep").Count.Should().Be(2);
    // 單一排程，時間為 BackupTime
    Regex.Matches(sql, "sp_add_jobschedule").Count.Should().Be(1);
    sql.Should().Contain("@active_start_time = 20000");
    // Step 1 成功即結束（還原預設不觸發）
    sql.Should().Contain("@on_success_action = 1");
    // 啟用還原的說明註解
    sql.Should().Contain("移至下一步");
}
```

（若測試檔尚未 `using System.Text.RegularExpressions;`，補上。）

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GenerateCreateBackupJob_合併還原"`
Expected: FAIL（目前無還原步驟、無說明註解）。

- [ ] **Step 3: 於 `GenerateCreateBackupJob` 插入還原 Step 2**

在 `GenerateCreateBackupJob` 內，備份 `sp_add_jobstep`（結尾 `sb.AppendLine($"';");` 之後、`// 建立排程` 之前）插入下列區塊。其還原命令主體取自原 `GenerateCreateRestoreJob`（462-549）：

```csharp
        // 說明：Step 1 成功後預設「結束並回報成功」，故 Step 2（還原）預設不執行。
        // 欲啟用每日還原：在 SSMS 將 Step 1 的「成功時動作」改為「移至下一步」。
        sb.AppendLine($"-- 說明：Step 1 成功後預設結束並回報成功，Step 2（還原）預設不執行。");
        sb.AppendLine($"-- 欲啟用每日還原：於 SSMS 將 Step 1「成功時動作」改為「移至下一步」。");
        sb.AppendLine($"-- 新增 Step: Restore Full to {testDbName}");
        sb.AppendLine($"EXEC dbo.sp_add_jobstep");
        sb.AppendLine($"    @job_name       = N'{escapedJobName}',");
        sb.AppendLine($"    @step_name      = N'Restore Full to {testDbName}',");
        sb.AppendLine($"    @subsystem      = N'TSQL',");
        sb.AppendLine($"    @on_success_action = 1,");
        sb.AppendLine($"    @on_fail_action    = 2,");
        sb.AppendLine($"    @command = N'");
        sb.AppendLine($"BEGIN TRY");
        sb.AppendLine($"    DECLARE @today     NVARCHAR(8)  = CONVERT(VARCHAR(8), GETDATE(), 112);");
        sb.AppendLine($"    DECLARE @fullPath  NVARCHAR(260) = N''{backupPath}{dbName}_FULL_'' + @today + ''.bak'';");
        sb.AppendLine();
        sb.AppendLine($"    PRINT N''開始：將 [{config.TestDatabaseName}] 設為 SINGLE_USER 並強制回滾...'';");
        sb.AppendLine($"    ALTER DATABASE [{config.TestDatabaseName}]");
        sb.AppendLine($"    SET SINGLE_USER");
        sb.AppendLine($"    WITH ROLLBACK IMMEDIATE;");
        sb.AppendLine();
        sb.AppendLine($"    PRINT N''開始執行還原到 [{config.TestDatabaseName}]，來源檔案 = '' + @fullPath + N''...'';");
        sb.AppendLine($"    RESTORE DATABASE [{config.TestDatabaseName}]");
        sb.AppendLine($"    FROM DISK = @fullPath");
        sb.AppendLine($"    WITH");
        sb.AppendLine($"      MOVE ''{dbName}_Data'' TO ''{restorePath}{testDbName}.mdf'',");
        sb.AppendLine($"      MOVE ''{dbName}_Log'' TO ''{restorePath}{testDbName}.ldf'',");
        sb.AppendLine($"      REPLACE,");
        sb.AppendLine($"      RECOVERY,");
        sb.AppendLine($"      STATS = 5;");
        sb.AppendLine($"    PRINT N''還原完成，開始切回 MULTI_USER...'';");
        sb.AppendLine();
        sb.AppendLine($"    ALTER DATABASE [{config.TestDatabaseName}]");
        sb.AppendLine($"    SET MULTI_USER;");
        sb.AppendLine($"    PRINT N''完成：已切回 MULTI_USER'';");
        sb.AppendLine($"END TRY");
        sb.AppendLine($"BEGIN CATCH");
        sb.AppendLine($"    PRINT N''錯誤: '' + ERROR_MESSAGE();");
        sb.AppendLine($"    THROW;");
        sb.AppendLine($"END CATCH");
        sb.AppendLine($"';");
        sb.AppendLine();
```

並於方法開頭補齊所需區域變數（若尚未宣告）：`var testDbName = EscapeSingleQuote(config.TestDatabaseName);`、`var restorePath = EscapeSingleQuote(config.RestorePath);`（`backupPath`、`dbName`、`escapedJobName` 已存在）。

同時把 `@description` 那行改為：
```csharp
        sb.AppendLine($"    @description = N'[Specurai] 每日對 {dbName} 做完整備份（含還原步驟，預設停用），保留 {config.RetentionDays} 天';");
```

- [ ] **Step 4: 移除 `GenerateCreateRestoreJob` 方法與分派**

- 刪除整個 `GenerateCreateRestoreJob`（462-549）方法。
- 於 `GenerateStepSql` 分派刪除該行：`MaintenancePlanStep.CreateRestoreJob => GenerateCreateRestoreJob(config, action),`（預設 `_ => string.Empty` 會接手，enum 值仍存在故編譯無誤）。

- [ ] **Step 5: `GenerateFullSql` 行內路徑合併**

- 於行內備份段（約 877-952），在備份 `sp_add_jobstep`（`@command = @backupCmd;` 之後、`-- 建立排程` 之前）插入還原 Step 2，使用行內動態 SQL 變數 `@restoreCmd`（沿用原行內還原段 993-1017 的命令內容），`@job_name = @jobName`，並加同樣的啟用說明註解：

```csharp
            sb.AppendLine("    -- 說明：Step 1 成功後預設結束並回報成功，Step 2（還原）預設不執行。");
            sb.AppendLine("    -- 欲啟用每日還原：於 SSMS 將 Step 1「成功時動作」改為「移至下一步」。");
            sb.AppendLine("    DECLARE @restoreCmd NVARCHAR(MAX) = N'");
            sb.AppendLine("BEGIN TRY");
            sb.AppendLine("    DECLARE @today     NVARCHAR(8)  = CONVERT(VARCHAR(8), GETDATE(), 112);");
            sb.AppendLine("    DECLARE @fullPath  NVARCHAR(260) = N''' + @BackupPath + @DatabaseName + N'_FULL_'' + @today + ''.bak'';");
            sb.AppendLine();
            sb.AppendLine("    PRINT N''開始：將 [' + @TestDatabaseName + N'] 設為 SINGLE_USER 並強制回滾...'';");
            sb.AppendLine("    ALTER DATABASE [' + @TestDatabaseName + N']");
            sb.AppendLine("    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;");
            sb.AppendLine();
            sb.AppendLine("    PRINT N''開始執行還原到 [' + @TestDatabaseName + N']...'';");
            sb.AppendLine("    RESTORE DATABASE [' + @TestDatabaseName + N']");
            sb.AppendLine("    FROM DISK = @fullPath");
            sb.AppendLine("    WITH");
            sb.AppendLine("      MOVE N''' + @DatabaseName + N'_Data'' TO N''' + @RestorePath + @TestDatabaseName + N'.mdf'',");
            sb.AppendLine("      MOVE N''' + @DatabaseName + N'_Log'' TO N''' + @RestorePath + @TestDatabaseName + N'.ldf'',");
            sb.AppendLine("      REPLACE, RECOVERY, STATS = 5;");
            sb.AppendLine();
            sb.AppendLine("    ALTER DATABASE [' + @TestDatabaseName + N']");
            sb.AppendLine("    SET MULTI_USER;");
            sb.AppendLine("    PRINT N''還原完成，已切回 MULTI_USER'';");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine("    PRINT N''錯誤: '' + ERROR_MESSAGE();");
            sb.AppendLine("    THROW;");
            sb.AppendLine("END CATCH';");
            sb.AppendLine();
            sb.AppendLine("    EXEC dbo.sp_add_jobstep");
            sb.AppendLine("        @job_name       = @jobName,");
            sb.AppendLine("        @step_name      = N'Restore Full',");
            sb.AppendLine("        @subsystem      = N'TSQL',");
            sb.AppendLine("        @on_success_action = 1,");
            sb.AppendLine("        @on_fail_action    = 2,");
            sb.AppendLine("        @command        = @restoreCmd;");
            sb.AppendLine();
```

- 刪除整個行內還原段（約 954-1049，`// 步驟 6: 建立還原排程 Job` 至該段 `GO`／空行結束，含 `restoreStep`、`@restoreJobName`、`@restoreScheduleName`、`@RestoreTime` 排程）。
- 若行內備份段的 `@description` 為 `N'[Specurai] 每日完整備份'`，改為 `N'[Specurai] 每日完整備份（含還原步驟，預設停用）'`。

- [ ] **Step 6: 更新 `GenerateFullSql` 相關測試並跑全 Infra 測試（GREEN）**

調整/補上 `GenerateFullSql` 對應斷言（單一備份 Job 含兩 `sp_add_jobstep`、無 `_FullRestore`、無 `@active_start_time = @RestoreTime`）。

Run: `dotnet test tests/Specurai.Infrastructure.Tests/Specurai.Infrastructure.Tests.csproj`
Expected: 全數通過。

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Infrastructure/Services/MaintenancePlanSqlGenerator.cs tests/Specurai.Infrastructure.Tests/Services/MaintenancePlanSqlGeneratorTests.cs
git commit -m "feat: 維護計劃備份 Job 併入還原步驟並移除獨立還原 Job（產生器）"
```

---

### Task 2: Application 移除還原 Job 的檢查與執行

**Files:**
- Modify: `src/Specurai.Application/Services/MaintenancePlanService.cs`
- Modify: `src/Specurai.Application/Models/StepCheckResult.cs`
- Test: `tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs`

**Interfaces:**
- Consumes: `IMaintenancePlanSqlGenerator`（Task 1 後備份 Job 已含還原步驟）。
- Produces: `CheckStepsAsync`/`ExecutePlanAsync` 不再處理 `CreateRestoreJob`。enum 值仍存在（Task 4 才移除）。

- [ ] **Step 1: 更新測試（RED）**

於 `MaintenancePlanServiceTests.cs`：
- 找出斷言「執行計劃會為還原 Job 產生 SQL」或「CheckSteps 回傳 CreateRestoreJob 結果」的測試，改為斷言**不再**發生。範例：

```csharp
[Fact]
public async Task ExecutePlanAsync_不再單獨執行還原Job()
{
    // Arrange
    var results = new List<StepCheckResult>
    {
        new() { Step = MaintenancePlanStep.CreateBackupJob, SelectedAction = "執行", AlreadyExists = false, CurrentStatus = "" }
    };
    _sqlGenerator.GenerateStepSql(MaintenancePlanStep.CreateBackupJob, Arg.Any<MaintenancePlanConfig>(), Arg.Any<string>())
        .Returns("-- backup sql");

    // Act
    await _sut.ExecutePlanAsync(CreateConfig(), results);

    // Assert：不對還原步驟產生 SQL
    _sqlGenerator.DidNotReceive().GenerateStepSql(MaintenancePlanStep.CreateRestoreJob, Arg.Any<MaintenancePlanConfig>(), Arg.Any<string>());
}
```

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj --filter "FullyQualifiedName~ExecutePlanAsync_不再單獨執行還原Job"`
Expected: FAIL。

- [ ] **Step 3: 移除 `CheckStepsAsync` 還原分派**

刪除 `CheckStepsAsync` switch 內：
```csharp
MaintenancePlanStep.CreateRestoreJob => await CheckJobAsync(config, MaintenancePlanStep.CreateRestoreJob, $"{config.DatabaseName}_FullRestore", ct),
```

- [ ] **Step 4: 移除 `ExecutePlanAsync` 還原群組**

刪除「交易群組 3：還原 Job」整段（含其上一個 `ct.ThrowIfCancellationRequested();` 其一即可保留於 CheckDb 群組後）：
```csharp
        // 交易群組 3：還原 Job
        var restoreStep = checkResults.FirstOrDefault(r => r.Step == MaintenancePlanStep.CreateRestoreJob && r.SelectedAction != "跳過");
        if (restoreStep != null)
        {
            progress?.Report("正在建立還原排程...");
            var sql = _sqlGenerator.GenerateStepSql(MaintenancePlanStep.CreateRestoreJob, config, restoreStep.SelectedAction);
            await _dbInfoRepo.ExecuteSqlAsync(sql, ct);
            progress?.Report("還原排程建立完成。");
        }
```

- [ ] **Step 5: 移除 `StepCheckResult` 顯示名稱對應**

於 `StepCheckResult.cs` 刪除：
```csharp
MaintenancePlanStep.CreateRestoreJob => "建立還原排程",
```

- [ ] **Step 6: 執行測試（GREEN）**

Run: `dotnet test tests/Specurai.Application.Tests/Specurai.Application.Tests.csproj`
Expected: 全數通過。

- [ ] **Step 7: Commit**

```bash
git add src/Specurai.Application/Services/MaintenancePlanService.cs src/Specurai.Application/Models/StepCheckResult.cs tests/Specurai.Application.Tests/Services/MaintenancePlanServiceTests.cs
git commit -m "feat: 維護計劃服務移除獨立還原 Job 的檢查與執行"
```

---

### Task 3: Desktop VM 與 View 移除還原勾選與還原時間

**Files:**
- Modify: `src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs`
- Modify: `src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml`
- Test: `tests/Specurai.Desktop.Tests/ViewModels/MaintenancePlanDocumentViewModelTests.cs`

**Interfaces:**
- Consumes: 無新相依。
- Produces: 步驟2 單一備份勾選；步驟1 無還原時間；`GetSelectedSteps` 不含 `CreateRestoreJob`。enum 值仍存在（Task 4 才移除）。

- [ ] **Step 1: 更新測試（RED）**

於 `MaintenancePlanDocumentViewModelTests.cs`：
- 移除/改寫斷言 `IsCreateRestoreJobSelected`、`RestoreJobStatus`、`RestoreTime`、以及 `GetSelectedSteps` 含 `CreateRestoreJob` 的測試。
- 若有測試驗證 `GetSelectedSteps`，新增/更新斷言其**不含** `CreateRestoreJob`（可透過既有公開路徑或行為驗證；若 `GetSelectedSteps` 為 private，改以既有可觀察行為斷言）。

- [ ] **Step 2: 執行測試確認失敗**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj --filter "FullyQualifiedName~MaintenancePlanDocumentViewModelTests"`
Expected: FAIL 或編譯失敗（移除的成員仍被測試引用）。

- [ ] **Step 3: VM 移除還原相關成員**

於 `MaintenancePlanDocumentViewModel.cs`：
- 刪除 `[ObservableProperty] private TimeSpan _restoreTime = new(3, 0, 0);`
- 刪除 `[ObservableProperty] private bool _isCreateRestoreJobSelected;`
- 刪除 `[ObservableProperty] private string _restoreJobStatus = string.Empty;`
- `RunStep2ChecksAsync` 移除 `case MaintenancePlanStep.CreateRestoreJob:` 整段（`RestoreJobStatus = ...; IsCreateRestoreJobSelected = false;`）。
- `GetSelectedSteps` 移除 `if (IsCreateRestoreJobSelected) steps.Add(MaintenancePlanStep.CreateRestoreJob);`
- `BuildConfig`（兩處）將 `RestoreTime = (int)(RestoreTime.Hours * 10000 + RestoreTime.Minutes * 100)` 與 `RestoreTime = (int)RestoreTime.TotalHours` 改為 `RestoreTime = 0`（UI 不再提供；欄位保留為 no-op）。

- [ ] **Step 4: View 調整步驟1與步驟2**

於 `MaintenancePlanDocumentView.axaml`：
- **步驟1**：移除「還原時間」的標籤與 `TimePicker`/輸入控件（與「備份時間」並排者），版面調整為僅保留備份時間與保留天數。
- **步驟2**：
  - 「建立每日全備份排程」CheckBox 內容文字改為「建立每日全備份排程（含還原步驟，預設停用）」。
  - 移除「建立每日還原排程（選填）」CheckBox 及其狀態文字（綁 `RestoreJobStatus` 者）。

- [ ] **Step 5: 執行測試（GREEN）＋建置**

Run: `dotnet test tests/Specurai.Desktop.Tests/Specurai.Desktop.Tests.csproj`
Expected: 全數通過。
Run: `dotnet build src/Specurai.Desktop/Specurai.Desktop.csproj`
Expected: 建置成功、無 XAML 綁定殘留 `RestoreJobStatus`/`RestoreTime`/`IsCreateRestoreJobSelected`。
（若被鎖：先 `taskkill //F //IM Specurai.Desktop.exe`。）

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Desktop/ViewModels/MaintenancePlanDocumentViewModel.cs src/Specurai.Desktop/Views/MaintenancePlanDocumentView.axaml tests/Specurai.Desktop.Tests/ViewModels/MaintenancePlanDocumentViewModelTests.cs
git commit -m "feat: 維護計劃步驟2合併備份還原勾選、步驟1移除還原時間"
```

---

### Task 4: 移除 `CreateRestoreJob` enum 並收尾 CLI/MCP/Domain

**Files:**
- Modify: `src/Specurai.Domain/Enums/MaintenancePlanStep.cs`
- Modify: `src/Specurai.Domain/Entities/MaintenancePlanConfig.cs`
- Modify: `src/Specurai.Cli/Commands/MaintenanceCommand.cs`
- Test: `tests/Specurai.Domain.Tests/Entities/MaintenancePlanConfigTests.cs`（如有引用）
- 全案建置驗證

**Interfaces:**
- Consumes: 無。
- Produces: `MaintenancePlanStep` 不再含 `CreateRestoreJob`；全案無殘留引用。

- [ ] **Step 1: 移除 enum 值**

於 `MaintenancePlanStep.cs` 刪除 `CreateRestoreJob` 列舉值與其 XML 註解。

- [ ] **Step 2: Domain config 註解**

於 `MaintenancePlanConfig.cs` 的 `RestoreTime` 屬性 XML 註解補「（已不用於排程；還原併入備份 Job 的第二步）」。（欄位本身保留。）

- [ ] **Step 3: CLI 解析防護**

於 `MaintenanceCommand.cs` 確認 `SelectedSteps` 字串→enum 解析（約 153 行）對無法解析的名稱**略過而非丟例外**（使用 `Enum.TryParse` 並過濾失敗項）。若現行未防護，改為：

```csharp
var steps = (raw.SelectedSteps ?? Array.Empty<string>())
    .Select(s => Enum.TryParse<MaintenancePlanStep>(s, out var v) ? (MaintenancePlanStep?)v : null)
    .Where(v => v.HasValue)
    .Select(v => v!.Value)
    .ToList();
```

（若現行已是此模式，本步驟無需改動，僅確認。）

- [ ] **Step 4: 全案建置**

Run: `dotnet build`
Expected: 建置成功、無任何 `CreateRestoreJob` 引用殘留（若有殘留編譯錯誤，逐一清除）。

- [ ] **Step 5: 全案測試**

Run: `dotnet test`
Expected: 全數通過（Domain/Application/Infrastructure/Desktop/McpServer/Cli）。
（若被鎖：先 `taskkill //F //IM Specurai.Desktop.exe`。）

- [ ] **Step 6: Commit**

```bash
git add src/Specurai.Domain/Enums/MaintenancePlanStep.cs src/Specurai.Domain/Entities/MaintenancePlanConfig.cs src/Specurai.Cli/Commands/MaintenanceCommand.cs
git add tests/Specurai.Domain.Tests/Entities/MaintenancePlanConfigTests.cs 2>/dev/null || true
git commit -m "feat: 移除 CreateRestoreJob 步驟並收尾 CLI/MCP/Domain"
```

---

## 手動驗收（實作完成後，非自動化）

1. 開啟維護計劃頁 → 精靈步驟1：確認無「還原時間」欄。
2. 步驟2：僅一個「建立每日全備份排程（含還原步驟，預設停用）」勾選，無獨立還原勾選。
3. 步驟3 預覽 SQL：單一 Job `{DB}_{RM}Backup`、兩個 `sp_add_jobstep`（Full Backup、Restore Full）、單一排程於備份時間、含啟用還原的說明註解、無 `_FullRestore`。
4. （選）於測試伺服器執行後，SSMS 檢視該 Job 有兩步驟，Step 1 成功時動作為「結束並回報成功」。
