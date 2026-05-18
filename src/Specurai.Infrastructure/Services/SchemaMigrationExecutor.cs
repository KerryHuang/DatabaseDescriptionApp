using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Specurai.Application.Services;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// Schema Migration 執行器（直接對 SQL Server 執行）
/// </summary>
public class SchemaMigrationExecutor : ISchemaMigrationExecutor
{
    public async Task<MigrationReport> ExecuteAsync(
        SyncScript script,
        string targetConnectionString,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var report = new MigrationReport
        {
            TargetEnvironment = script.TargetEnvironment,
            ExecutedAt = DateTime.Now,
            AppliedScript = script.ApplyScript,
            IsDryRun = dryRun
        };

        foreach (var diff in script.Differences)
        {
            report.Entries.Add(new MigrationLogEntry
            {
                ObjectName = diff.ObjectName,
                Action = GetActionText(diff),
                Status = MigrationLogStatus.Success
            });
        }

        var sqlToRun = dryRun ? script.DryRunScript : script.ApplyScript;
        var batches = SplitOnGo(sqlToRun);
        var sw = Stopwatch.StartNew();
        Exception? failure = null;
        string? failedBatch = null;

        try
        {
            await using var connection = new SqlConnection(targetConnectionString);
            await connection.OpenAsync(ct);

            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                try
                {
                    await using var command = new SqlCommand(batch, connection);
                    command.CommandTimeout = 600;
                    await command.ExecuteNonQueryAsync(ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failure = ex;
                    failedBatch = batch;
                    break; // 失敗即停；idempotent 腳本可在修正後重跑
                }
            }
        }
        catch (OperationCanceledException) { throw; }

        sw.Stop();
        report.TotalDuration = sw.Elapsed;

        if (failure == null)
        {
            report.IsSuccess = true;
            var avg = report.Entries.Count > 0
                ? TimeSpan.FromTicks(sw.Elapsed.Ticks / report.Entries.Count)
                : sw.Elapsed;
            foreach (var entry in report.Entries) entry.Duration = avg;
        }
        else
        {
            report.IsSuccess = false;
            report.ErrorMessage = failure.Message;
            if (failure is SqlException sqlEx && sqlEx.LineNumber > 0 && failedBatch != null)
                report.FailedStatement =
                    $"第 {sqlEx.LineNumber} 行附近（失敗批次）：\n{ExtractFailingStatement(failedBatch, sqlEx.LineNumber)}";
            else if (failedBatch != null)
                report.FailedStatement = $"失敗批次：\n{TruncateForReport(failedBatch)}";

            // 已執行批次無外層 transaction → 已 commit；尚未跑到的標 Skipped
            var pending = report.Entries.Where(e => e.Status == MigrationLogStatus.Success).ToList();
            if (pending.Count > 0)
            {
                pending[0].Status = MigrationLogStatus.Failed;
                pending[0].ErrorMessage = failure.Message;
                foreach (var entry in pending.Skip(1))
                {
                    entry.Status = MigrationLogStatus.Skipped;
                    entry.ErrorMessage = "因前一個批次失敗，後續批次未執行；修正錯誤後可重跑（腳本為 idempotent）";
                }
            }
        }

        return report;
    }

    /// <summary>
    /// 將 T-SQL 腳本以 `GO`（獨立一行，前後可有空白）為界切成多個批次。
    /// 與 SSMS 一致；GO 不是 T-SQL 語句，必須由執行端分離。
    /// </summary>
    private static IList<string> SplitOnGo(string script)
    {
        var batches = new List<string>();
        var current = new StringBuilder();
        var lines = script.Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (GoLineRegex.IsMatch(line))
            {
                if (current.Length > 0)
                {
                    batches.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.AppendLine(line);
            }
        }
        if (current.Length > 0) batches.Add(current.ToString());
        return batches;
    }

    private static readonly Regex GoLineRegex = new(
        @"^\s*GO\s*(?:--.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<ResizeLogResult> ResizeLogAsync(
        string targetConnectionString, int targetSizeMb, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new SqlConnection(targetConnectionString);
            await connection.OpenAsync(ct);

            // 取得 log 邏輯檔名、log_reuse_wait 與目前大小
            string? logName = null;
            string? logReuseWait = null;
            double beforeMb = 0;
            await using (var cmd = new SqlCommand(
                @"SELECT f.name, d.log_reuse_wait_desc, f.size * 8.0 / 1024 AS size_mb
                  FROM sys.database_files f
                  CROSS APPLY sys.databases d
                  WHERE f.type_desc = 'LOG' AND d.database_id = DB_ID()", connection))
            await using (var rd = await cmd.ExecuteReaderAsync(ct))
            {
                if (await rd.ReadAsync(ct))
                {
                    logName = rd.GetString(0);
                    logReuseWait = rd.GetString(1);
                    beforeMb = rd.GetDouble(2);
                }
            }

            if (logName == null)
                return new ResizeLogResult { IsSuccess = false, ErrorMessage = "找不到 log 檔案" };

            var safeLogName = logName.Replace("'", "''");
            string operation;

            if (Math.Abs(beforeMb - targetSizeMb) < 1)
            {
                operation = "NoChange";
            }
            else if (targetSizeMb > beforeMb)
            {
                // 預擴：ALTER DATABASE ... MODIFY FILE，避免大量 migration 時 autogrow 卡寫入
                var dbName = connection.Database.Replace("]", "]]");
                await using var cmd = new SqlCommand(
                    $"ALTER DATABASE [{dbName}] MODIFY FILE (NAME = N'{safeLogName}', SIZE = {targetSizeMb}MB);", connection);
                cmd.CommandTimeout = 600;
                await cmd.ExecuteNonQueryAsync(ct);
                operation = "Grow";
            }
            else
            {
                // 縮小：CHECKPOINT 釋放可重用 log → DBCC SHRINKFILE 回收物理空間
                await using (var cmd = new SqlCommand("CHECKPOINT;", connection))
                {
                    cmd.CommandTimeout = 120;
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                await using (var cmd = new SqlCommand(
                    $"DBCC SHRINKFILE(N'{safeLogName}', {targetSizeMb}) WITH NO_INFOMSGS;", connection))
                {
                    cmd.CommandTimeout = 600;
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                operation = "Shrink";
            }

            // 取得調整後大小
            double afterMb = beforeMb;
            await using (var cmd = new SqlCommand(
                "SELECT size * 8.0 / 1024 FROM sys.database_files WHERE type_desc = 'LOG'", connection))
            {
                var v = await cmd.ExecuteScalarAsync(ct);
                if (v != null && v != DBNull.Value) afterMb = Convert.ToDouble(v);
            }

            return new ResizeLogResult
            {
                IsSuccess = true,
                BeforeSizeMb = beforeMb,
                AfterSizeMb = afterMb,
                Operation = operation,
                LogReuseWait = logReuseWait
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ResizeLogResult { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    private static string TruncateForReport(string s) =>
        s.Length <= 2000 ? s : s.Substring(0, 2000) + "\n...（已截斷）";

    /// <summary>
    /// 從腳本中提取失敗行附近的 SQL 語句（前後各 3 行）
    /// </summary>
    private static string ExtractFailingStatement(string script, int failLine)
    {
        var lines = script.Split('\n');
        if (failLine <= 0 || failLine > lines.Length) return string.Empty;

        var from = Math.Max(0, failLine - 4);
        var to = Math.Min(lines.Length - 1, failLine + 2);

        var sb = new StringBuilder();
        for (var i = from; i <= to; i++)
        {
            var marker = i == failLine - 1 ? "→ " : "  ";
            sb.AppendLine($"{marker}{lines[i]}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string GetActionText(SchemaDifference diff)
    {
        return diff.DifferenceType switch
        {
            DifferenceType.Added => diff.ObjectType switch
            {
                SchemaObjectType.Table => "CREATE TABLE",
                SchemaObjectType.Column => "ADD COLUMN",
                SchemaObjectType.Index => "CREATE INDEX",
                SchemaObjectType.Constraint => "ADD CONSTRAINT",
                SchemaObjectType.View => "CREATE VIEW",
                SchemaObjectType.StoredProcedure => "CREATE PROCEDURE",
                SchemaObjectType.Function => "CREATE FUNCTION",
                SchemaObjectType.Trigger => "CREATE TRIGGER",
                _ => "ADD"
            },
            DifferenceType.Modified => "ALTER " + diff.ObjectType.ToString().ToUpper(),
            _ => diff.DifferenceType.ToString()
        };
    }
}
