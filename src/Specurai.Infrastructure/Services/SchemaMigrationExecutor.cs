using System.Diagnostics;
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

        var sw = Stopwatch.StartNew();
        try
        {
            await using var connection = new SqlConnection(targetConnectionString);
            await connection.OpenAsync(ct);

            // Dry Run：包在 transaction 中執行後強制 ROLLBACK，驗證腳本是否有語法錯誤
            if (dryRun)
            {
                await using var txn = connection.BeginTransaction();
                await using var command = new SqlCommand(script.ApplyScript, connection, txn);
                command.CommandTimeout = 300;
                await command.ExecuteNonQueryAsync(ct);
                await txn.RollbackAsync(ct);
            }
            else
            {
                await using var command = new SqlCommand(script.ApplyScript, connection);
                command.CommandTimeout = 300;
                await command.ExecuteNonQueryAsync(ct);
            }

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

            foreach (var entry in report.Entries.Where(e => e.Status == MigrationLogStatus.Success))
            {
                entry.Status = MigrationLogStatus.Failed;
                entry.ErrorMessage = ex.Message;
            }
        }

        return report;
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
