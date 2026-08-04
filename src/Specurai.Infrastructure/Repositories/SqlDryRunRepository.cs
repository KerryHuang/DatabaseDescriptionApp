using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Repositories;

/// <summary>
/// SQL Dry Run Repository 實作：在交易中執行單一 DML 擷取預演結果。
/// 同時實作 <see cref="ISqlDryRunRepository"/>（一律 ROLLBACK）與
/// <see cref="ISqlDmlExecuteRepository"/>（成功時 COMMIT），兩者共用同一套
/// 解析、OUTPUT 注入與 Trigger fallback 邏輯，只有交易收尾方式不同。
/// </summary>
public class SqlDryRunRepository : ISqlDryRunRepository, ISqlDmlExecuteRepository
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

    public Task<DryRunResult> DryRunAsync(string sql, string connectionString, CancellationToken ct = default)
        => RunAsync(sql, connectionString, commit: false, ct);

    public async Task<DryRunResult> ExecuteAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("未設定資料庫連線");

        return await ExecuteAsync(sql, connectionString, ct);
    }

    public Task<DryRunResult> ExecuteAsync(string sql, string connectionString, CancellationToken ct = default)
        => RunAsync(sql, connectionString, commit: true, ct);

    /// <summary>
    /// 共用核心：解析、OUTPUT 注入、Trigger fallback 邏輯與 DryRunAsync 完全相同，
    /// 差異只在交易收尾——commit 為 true 時成功即 COMMIT，否則一律 ROLLBACK。
    /// </summary>
    private async Task<DryRunResult> RunAsync(string sql, string connectionString, bool commit, CancellationToken ct)
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
        if (!commit && analysis.StatementType == DryRunStatementType.Insert)
            warnings.Add("若目標資料表有 IDENTITY 欄位，序號在回滾後仍會被消耗。");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // 使用者已自帶 OUTPUT INTO：結果寫入指定目標而非回傳結果集，
        // 沿用原句改走 ExecuteCountOnlyAsync，只回報影響筆數、無前後對照預覽
        if (analysis.HasUserOutputIntoClause)
        {
            warnings.Add("使用者自帶 OUTPUT INTO，結果已寫入指定目標（將隨交易回滾），僅回報影響筆數。");
            return await ExecuteCountOnlyAsync(connection, sql, analysis, warnings, commit, ct);
        }

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
            return await ExecutePreviewAsync(connection, rewrittenSql, analysis, warnings, commit, ct);
        }
        catch (SqlException ex) when (ex.Number == TriggerOutputErrorNumber && !analysis.HasUserOutputClause)
        {
            // 目標表有觸發程序：退回原句執行，只回報影響筆數
            warnings.Add("目標資料表有觸發程序（Trigger），無法提供前後資料對照，僅回報影響筆數。");
            return await ExecuteCountOnlyAsync(connection, sql, analysis, warnings, commit, ct);
        }
        catch (SqlException ex)
        {
            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                Warnings = warnings,
                ExecutionError = commit
                    ? $"執行失敗（已回滾）：{ex.Message}"
                    : $"此語句實際執行將會失敗：{ex.Message}"
            };
        }
    }

    /// <summary>
    /// 在交易中執行含 OUTPUT 的 DML，讀取前後對照後依 commit 決定 COMMIT 或 ROLLBACK
    /// </summary>
    private static async Task<DryRunResult> ExecutePreviewAsync(
        SqlConnection connection, string sql, SqlDryRunAnalysis analysis,
        List<string> warnings, bool commit, CancellationToken ct)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };

            var preview = new DataTable();
            var total = 0;
            // reader 必須在 Commit 前關閉，因此用區塊限制其生命週期
            {
                using var reader = await command.ExecuteReaderAsync(ct);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    // 未別名的 deleted.*/inserted.* 會產生重複欄位名稱，加序號避免 DataTable 衝突
                    if (preview.Columns.Contains(name))
                        name = $"{name}_{i}";
                    preview.Columns.Add(name, typeof(object));
                }

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
            }

            if (commit)
            {
                // 交易收尾不使用呼叫端的取消權杖，確保必定送出
                await transaction.CommitAsync(CancellationToken.None);
                committed = true;
            }

            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                AffectedRowCount = total,
                PreviewTable = preview,
                PreviewTruncated = total > PreviewRowLimit,
                Warnings = warnings,
                Committed = committed
            };
        }
        finally
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Trigger fallback：在交易中執行原句，只取得影響筆數後依 commit 決定 COMMIT 或 ROLLBACK
    /// </summary>
    private static async Task<DryRunResult> ExecuteCountOnlyAsync(
        SqlConnection connection, string sql, SqlDryRunAnalysis analysis,
        List<string> warnings, bool commit, CancellationToken ct)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
            var affected = await command.ExecuteNonQueryAsync(ct);

            if (commit)
            {
                await transaction.CommitAsync(CancellationToken.None);
                committed = true;
            }

            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                AffectedRowCount = affected,
                Warnings = warnings,
                Committed = committed
            };
        }
        catch (SqlException ex)
        {
            return new DryRunResult
            {
                IsValid = true,
                StatementType = analysis.StatementType,
                Warnings = warnings,
                ExecutionError = commit
                    ? $"執行失敗（已回滾）：{ex.Message}"
                    : $"此語句實際執行將會失敗：{ex.Message}"
            };
        }
        finally
        {
            if (!committed)
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
