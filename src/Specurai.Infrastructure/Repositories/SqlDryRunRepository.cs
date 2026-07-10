using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Repositories;

/// <summary>
/// SQL Dry Run Repository 實作：在交易中執行單一 DML 擷取預演結果，最後一律 ROLLBACK
/// </summary>
public class SqlDryRunRepository : ISqlDryRunRepository
{
    /// <summary>前後對照預覽筆數上限</summary>
    private const int PreviewRowLimit = 100;

    /// <summary>SQL Server 錯誤 334：目標表有觸發程序時，OUTPUT 子句（無 INTO）不允許使用</summary>
    private const int TriggerOutputErrorNumber = 334;

    private readonly Func<string?> _connectionStringProvider;
    private readonly SqlDryRunAnalyzer _analyzer = new();

    public SqlDryRunRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<DryRunResult> DryRunAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("未設定資料庫連線");

        return await DryRunAsync(sql, connectionString, ct);
    }

    public async Task<DryRunResult> DryRunAsync(string sql, string connectionString, CancellationToken ct = default)
    {
        // 離線解析與驗證：不通過就不連資料庫
        var analysis = _analyzer.Analyze(sql);
        if (!analysis.IsValid)
        {
            return new DryRunResult
            {
                IsValid = false,
                StatementType = analysis.StatementType,
                SyntaxErrors = analysis.SyntaxErrors,
                RejectReason = analysis.RejectReason
            };
        }

        var warnings = new List<string>();
        if (analysis.StatementType == DryRunStatementType.Insert)
            warnings.Add("若目標資料表有 IDENTITY 欄位，序號在回滾後仍會被消耗。");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // OUTPUT 注入：UPDATE 需先查目標表欄位以產生 舊_欄位/新_欄位 別名對照
        string rewrittenSql;
        if (analysis.HasUserOutputClause)
        {
            rewrittenSql = sql;
        }
        else if (analysis.StatementType == DryRunStatementType.Update)
        {
            var columns = await GetTableColumnsAsync(connection, analysis.TargetSchema, analysis.TargetTable, ct);
            if (columns.Count == 0)
                warnings.Add("無法解析目標資料表欄位，前後對照以 deleted/inserted 全欄位呈現。");
            rewrittenSql = _analyzer.RewriteWithOutput(sql, columns);
        }
        else
        {
            rewrittenSql = _analyzer.RewriteWithOutput(sql);
        }

        try
        {
            return await ExecutePreviewAsync(connection, rewrittenSql, analysis, warnings, ct);
        }
        catch (SqlException ex) when (ex.Number == TriggerOutputErrorNumber && !analysis.HasUserOutputClause)
        {
            // 目標表有觸發程序：退回原句執行，只回報影響筆數
            warnings.Add("目標資料表有觸發程序（Trigger），無法提供前後資料對照，僅回報影響筆數。");
            return await ExecuteCountOnlyAsync(connection, sql, analysis, warnings, ct);
        }
        catch (SqlException ex)
        {
            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                Warnings = warnings,
                ExecutionError = $"此語句實際執行將會失敗：{ex.Message}"
            };
        }
    }

    /// <summary>
    /// 在交易中執行含 OUTPUT 的 DML，讀取前後對照後回滾
    /// </summary>
    private static async Task<DryRunResult> ExecutePreviewAsync(
        SqlConnection connection, string sql, SqlDryRunAnalysis analysis,
        List<string> warnings, CancellationToken ct)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
            using var reader = await command.ExecuteReaderAsync(ct);

            var preview = new DataTable();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                // 未別名的 deleted.*/inserted.* 會產生重複欄位名稱，加序號避免 DataTable 衝突
                if (preview.Columns.Contains(name))
                    name = $"{name}_{i}";
                preview.Columns.Add(name, typeof(object));
            }

            var total = 0;
            while (await reader.ReadAsync(ct))
            {
                total++;
                if (total > PreviewRowLimit)
                    continue;

                var row = preview.NewRow();
                for (var i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                preview.Rows.Add(row);
            }

            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                AffectedRowCount = total,
                PreviewTable = preview,
                PreviewTruncated = total > PreviewRowLimit,
                Warnings = warnings
            };
        }
        finally
        {
            // 一律回滾（不使用呼叫端的取消權杖，確保回滾必定送出）
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Trigger fallback：在交易中執行原句，只取得影響筆數後回滾
    /// </summary>
    private static async Task<DryRunResult> ExecuteCountOnlyAsync(
        SqlConnection connection, string sql, SqlDryRunAnalysis analysis,
        List<string> warnings, CancellationToken ct)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
            var affected = await command.ExecuteNonQueryAsync(ct);

            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                AffectedRowCount = affected,
                Warnings = warnings
            };
        }
        catch (SqlException ex)
        {
            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                Warnings = warnings,
                ExecutionError = $"此語句實際執行將會失敗：{ex.Message}"
            };
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// 查詢目標資料表的欄位清單（依 column_id 排序）
    /// </summary>
    private static async Task<List<string>> GetTableColumnsAsync(
        SqlConnection connection, string? schema, string? table, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(table))
            return [];

        const string sql = @"
            SELECT c.name
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(@FullName)
            ORDER BY c.column_id";

        var escapedTable = table.Replace("]", "]]");
        var fullName = string.IsNullOrEmpty(schema)
            ? $"[{escapedTable}]"
            : $"[{schema.Replace("]", "]]")}].[{escapedTable}]";

        var result = await connection.QueryAsync<string>(
            new CommandDefinition(sql, new { FullName = fullName }, cancellationToken: ct));
        return result.ToList();
    }
}
