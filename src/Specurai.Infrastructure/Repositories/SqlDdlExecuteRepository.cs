using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Repositories;

/// <summary>
/// SQL DDL 執行 Repository 實作：離線驗證通過後在單一交易中依 GO 批次逐批執行，
/// 任一批失敗即整批回滾；commit=false 一律 ROLLBACK（預演）、commit=true 全部成功才 COMMIT。
/// SQL Server 物件級 DDL 皆為 transactional，回滾可靠。
/// </summary>
public class SqlDdlExecuteRepository : ISqlDdlExecuteRepository
{
    private readonly SqlDdlScriptAnalyzer _analyzer = new();

    public async Task<DdlExecutionResult> ExecuteAsync(
        string script, string connectionString, bool commit, CancellationToken ct = default)
    {
        // 離線解析與驗證：不通過就不連資料庫
        var analysis = _analyzer.Analyze(script);
        if (!analysis.IsValid)
        {
            return new DdlExecutionResult
            {
                IsValid = false,
                SyntaxErrors = analysis.SyntaxErrors,
                RejectReason = analysis.RejectReason
            };
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            for (var i = 0; i < analysis.Batches.Count; i++)
            {
                try
                {
                    await using var command = new SqlCommand(analysis.Batches[i], connection, transaction)
                    {
                        CommandTimeout = 60
                    };
                    await command.ExecuteNonQueryAsync(ct);
                }
                catch (SqlException ex)
                {
                    return new DdlExecutionResult
                    {
                        IsValid = true,
                        Statements = analysis.Statements,
                        FailedBatchIndex = i + 1,
                        ExecutionError = commit
                            ? $"第 {i + 1} 批執行失敗（整批已回滾）：{ex.Message}"
                            : $"第 {i + 1} 批實際執行將會失敗：{ex.Message}"
                    };
                }
            }

            if (commit)
            {
                try
                {
                    // 交易收尾不使用呼叫端的取消權杖，確保必定送出
                    await transaction.CommitAsync(CancellationToken.None);
                    committed = true;
                }
                catch (SqlException ex)
                {
                    // COMMIT 本身失敗（如提交過程中斷線）：結果不確定，不能宣稱已回滾
                    return new DdlExecutionResult
                    {
                        IsValid = true,
                        Statements = analysis.Statements,
                        ExecutionError = $"COMMIT 失敗，交易結果不確定，請查詢資料庫確認：{ex.Message}",
                        CommitUncertain = true
                    };
                }
            }

            return new DdlExecutionResult
            {
                IsValid = true,
                Statements = analysis.Statements,
                Committed = committed
            };
        }
        finally
        {
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (SqlException)
                {
                    // 本地 rollback 失敗不得蓋掉原始例外：連線已斷時 SQL Server 會自行回滾未提交交易，
                    // 本地 rollback 失敗不代表資料風險，吞掉即可
                }
            }
        }
    }
}
