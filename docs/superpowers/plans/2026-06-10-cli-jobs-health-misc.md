# B3 · CLI 其他對齊（jobs 非-Specurai / health export-sql）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 為 `Specurai.Cli` 補上 `jobs list --include-non-specurai`（一併列出未由 Specurai 管理的 Agent Job）與 `health export-sql`（產生健康監控安裝 SQL，可寫檔或輸出 stdout），對齊 MCP `list_non_specurai_jobs` 與 `export_health_monitoring_sql`。

**Architecture:** 純展示層接線，無服務/DI 變更。`jobs list` 加旗標後額外呼叫 `IAgentJobService.GetNonSpecuraiJobsAsync()` 併入結果並加「類型」欄；`health export-sql` 呼叫 `IHealthMonitoringService.GenerateExportSqlAsync()`。沿用既有 jobs/health 子命令「無單元測試（純接線）」慣例，以建置 + 手動煙霧測試驗證。

**Tech Stack:** .NET 8、System.CommandLine、Spectre.Console。

---

## File Structure

- Modify: `src/Specurai.Cli/Commands/JobsCommand.cs` — `list` 加 `--include-non-specurai` 旗標。
- Modify: `src/Specurai.Cli/Commands/HealthCommand.cs` — 新增 `export-sql` 子命令並於 `Create()` 註冊。

關鍵既有型別（已驗證）：
- `IAgentJobService.GetJobsAsync()` / `GetNonSpecuraiJobsAsync()` → `IReadOnlyList<AgentJobInfo>`；`AgentJobInfo { JobId, Name, Description, IsEnabled, LastRunDate, LastRunOutcome, NextRunDate, IsSpecuraiJob(computed), LastRunOutcomeText(computed) }`。
- `IHealthMonitoringService.GenerateExportSqlAsync(CancellationToken)` → `Task<string>`。

---

## Task 1: `jobs list --include-non-specurai`

**Files:** Modify `src/Specurai.Cli/Commands/JobsCommand.cs`

- [ ] **Step 1: 改寫 `CreateListCommand`**

將 `CreateListCommand()` 內容改為（保留既有預設行為，旗標開啟時併入非-Specurai 並加「類型」欄）：

```csharp
private static Command CreateListCommand()
{
    var includeNonSpecuraiOpt = new Option<bool>(
        "--include-non-specurai", "一併列出未由 Specurai 管理的 Agent Job");
    var command = new Command("list", "列出 Specurai 管理的 Job") { includeNonSpecuraiOpt };

    command.SetHandler(async (includeNonSpecurai) =>
    {
        var service = Program.Services.GetRequiredService<IAgentJobService>();
        var jobs = (await service.GetJobsAsync()).ToList();
        if (includeNonSpecurai)
            jobs.AddRange(await service.GetNonSpecuraiJobsAsync());

        if (CliOutput.JsonMode)
        {
            CliOutput.Success(jobs, jobs.Count);
            return;
        }

        if (jobs.Count == 0) { CliOutput.Info("沒有 Agent Job。"); return; }

        var table = new Table().Title("Agent Jobs");
        if (includeNonSpecurai) table.AddColumn("類型");
        table.AddColumn("名稱");
        table.AddColumn("狀態");
        table.AddColumn("上次執行");
        table.AddColumn("結果");
        table.AddColumn("下次執行");

        foreach (var j in jobs)
        {
            var cells = new List<string>();
            if (includeNonSpecurai)
                cells.Add(j.IsSpecuraiJob ? "[blue]Specurai[/]" : "[grey]其他[/]");
            cells.Add(j.Name.EscapeMarkup());
            cells.Add(j.IsEnabled ? "[green]啟用[/]" : "[grey]停用[/]");
            cells.Add(j.LastRunDate?.ToString("yyyy-MM-dd HH:mm") ?? "從未");
            cells.Add(j.LastRunOutcomeText.EscapeMarkup());
            cells.Add(j.NextRunDate?.ToString("yyyy-MM-dd HH:mm") ?? "N/A");
            table.AddRow(cells.ToArray());
        }

        AnsiConsole.Write(table);
    }, includeNonSpecuraiOpt);

    return command;
}
```

- [ ] **Step 2: 建置確認**

Run: `dotnet build src/Specurai.Cli/Specurai.Cli.csproj`
Expected: Build succeeded，0 error。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.Cli/Commands/JobsCommand.cs
git commit -m "feat(cli): jobs list --include-non-specurai 對齊 MCP list_non_specurai_jobs"
```

---

## Task 2: `health export-sql`

**Files:** Modify `src/Specurai.Cli/Commands/HealthCommand.cs`

- [ ] **Step 1: 新增 `CreateExportSqlCommand`**

在 `HealthCommand` class 內新增：

```csharp
private static Command CreateExportSqlCommand()
{
    var outputOpt = new Option<string?>(["--output", "-o"], "輸出檔案路徑（不指定則輸出至 stdout）");
    var command = new Command("export-sql", "產生健康監控安裝 SQL 腳本") { outputOpt };

    command.SetHandler(async (output) =>
    {
        var service = Program.Services.GetRequiredService<IHealthMonitoringService>();

        string sql;
        try
        {
            sql = await service.GenerateExportSqlAsync();
        }
        catch (Exception ex)
        {
            CliOutput.Error($"產生 SQL 腳本失敗：{ex.Message}");
            Environment.ExitCode = 1;
            return;
        }

        if (!string.IsNullOrEmpty(output))
        {
            await File.WriteAllTextAsync(output, sql);
            if (CliOutput.JsonMode)
                CliOutput.Success(new { Output = output, Length = sql.Length });
            else
                CliOutput.SuccessMessage($"已輸出健康監控安裝 SQL 至 {output}");
        }
        else
        {
            if (CliOutput.JsonMode)
                CliOutput.Success(new { Sql = sql });
            else
                Console.WriteLine(sql);
        }
    }, outputOpt);

    return command;
}
```

在 `Create()` 內加入註冊（與既有 `AddCommand(...)` 並列）：

```csharp
command.AddCommand(CreateExportSqlCommand());
```

- [ ] **Step 2: 建置確認**

Run: `dotnet build src/Specurai.Cli/Specurai.Cli.csproj`
Expected: Build succeeded，0 error。

- [ ] **Step 3: Commit**

```bash
git add src/Specurai.Cli/Commands/HealthCommand.cs
git commit -m "feat(cli): health export-sql 對齊 MCP export_health_monitoring_sql"
```

---

## Task 3: 整批驗證與審查

- [ ] **Step 1: 命令樹確認**

Run: `dotnet run --project src/Specurai.Cli/Specurai.Cli.csproj -- jobs list --help`
Expected: 出現 `--include-non-specurai`。
Run: `dotnet run --project src/Specurai.Cli/Specurai.Cli.csproj -- health --help`
Expected: 出現 `export-sql`。

- [ ] **Step 2: 煙霧測試（需資料庫連線）**

Run:
```
dotnet run --project src/Specurai.Cli/Specurai.Cli.csproj -- jobs list --include-non-specurai
dotnet run --project src/Specurai.Cli/Specurai.Cli.csproj -- health export-sql -o /tmp/health-install.sql
```
Expected：`jobs list` 顯示含「類型」欄的清單（或「沒有 Agent Job」）；`health export-sql` 產生檔案並回報。事後刪除測試檔。

- [ ] **Step 3: Cli 測試全綠（確認未破壞既有）**

Run: `dotnet test tests/Specurai.Cli.Tests/Specurai.Cli.Tests.csproj`
Expected: 全部 PASS。

- [ ] **Step 4: 程式碼審查**

使用 `superpowers:requesting-code-review` 技能審查本批變更，通過後回報。B3 完成後 CLI 缺口即全數補齊，接著進入 B4/B5（MCP 運維工具）。

---

## Self-Review 紀錄

- **Spec 覆蓋**：B3 範圍（jobs 非-Specurai、health export-sql）皆有對應 Task。✅
- **Placeholder 掃描**：無 TBD/TODO；每個程式碼步驟均含完整程式碼。✅
- **型別一致性**：`GetNonSpecuraiJobsAsync()→IReadOnlyList<AgentJobInfo>`、`GenerateExportSqlAsync()→Task<string>` 與既有定義相符；`AgentJobInfo.IsSpecuraiJob`/`LastRunOutcomeText` 為既有 computed 屬性。✅
- **相容性**：`jobs list` 不帶旗標時輸出與既有完全一致（5 欄、無「類型」欄）；旗標開啟才加欄並併入非-Specurai。
- **刻意取捨**：沿用既有 jobs/health 子命令「純接線無單元測試」慣例；`health export-sql` 支援 `-o` 寫檔（較 MCP 僅回字串更實用），不指定則輸出 stdout。
