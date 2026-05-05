using System.Diagnostics;
using System.Text;
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
        var sw = Stopwatch.StartNew();
        try
        {
            await using var connection = new SqlConnection(targetConnectionString);
            await connection.OpenAsync(ct);

            // Dry Run 使用 DryRunScript（腳本內部強制 ROLLBACK），不需外層包 ADO.NET transaction
            // 避免巢狀 transaction 造成 @@TRANCOUNT 計數混亂
            await using var command = new SqlCommand(sqlToRun, connection);
            command.CommandTimeout = 300;
            await command.ExecuteNonQueryAsync(ct);

            sw.Stop();
            report.TotalDuration = sw.Elapsed;
            report.IsSuccess = true;

            var avgDuration = report.Entries.Count > 0
                ? TimeSpan.FromTicks(sw.Elapsed.Ticks / report.Entries.Count)
                : sw.Elapsed;
            foreach (var entry in report.Entries)
                entry.Duration = avgDuration;
        }
        catch (Exception ex)
        {
            sw.Stop();
            report.TotalDuration = sw.Elapsed;
            report.IsSuccess = false;
            report.ErrorMessage = ex.Message;

            // 從 SqlException 行號提取失敗語句，幫助診斷根因
            if (ex is SqlException sqlEx && sqlEx.LineNumber > 0)
            {
                report.FailedStatement = ExtractFailingStatement(sqlToRun, sqlEx.LineNumber);
                report.ErrorMessage = $"{sqlEx.Message}\n\n▶ 失敗語句（第 {sqlEx.LineNumber} 行）：\n{report.FailedStatement}";
            }

            var pending = report.Entries.Where(e => e.Status == MigrationLogStatus.Success).ToList();
            if (pending.Count > 0)
            {
                // 第一個項目顯示實際錯誤，其餘標為「已回滾」（它們其實根本沒跑到，或被回滾）
                pending[0].Status = MigrationLogStatus.Failed;
                pending[0].ErrorMessage = ex.Message;
                foreach (var entry in pending.Skip(1))
                {
                    entry.Status = MigrationLogStatus.RolledBack;
                    entry.ErrorMessage = "因前一條語句失敗，整個事務已自動回滾";
                }
            }
        }

        return report;
    }

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
