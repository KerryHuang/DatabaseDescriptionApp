using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// 唯讀 SQL 驗證結果
/// </summary>
public class SqlReadOnlyValidationResult
{
    /// <summary>批次是否僅含允許的唯讀語句</summary>
    public required bool IsValid { get; init; }

    /// <summary>拒絕原因（通過時為 null）</summary>
    public string? RejectReason { get; init; }
}

/// <summary>
/// 唯讀 SQL 驗證器：以 ScriptDom 解析整個批次，逐句白名單檢查（純離線，不碰資料庫）。
/// 允許：SELECT（不含 INTO）、DECLARE、變數 SET、工作階段 SET 選項、SET ISOLATION LEVEL。
/// 其餘（DML/DDL/EXEC/MERGE/TRUNCATE 等）一律拒絕；EXEC 因無法靜態判斷 SP 內容是否唯讀，一律拒絕。
/// </summary>
public class SqlReadOnlyValidator
{
    public SqlReadOnlyValidationResult Validate(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(sql), out var parseErrors);

        if (parseErrors.Count > 0)
        {
            var e = parseErrors[0];
            return new SqlReadOnlyValidationResult
            {
                IsValid = false,
                RejectReason = $"SQL 語法錯誤（第 {e.Line} 行第 {e.Column} 列）：{e.Message}"
            };
        }

        var statements = ((TSqlScript)fragment).Batches
            .SelectMany(b => b.Statements)
            .ToList();

        if (statements.Count == 0)
        {
            return new SqlReadOnlyValidationResult
            {
                IsValid = false,
                RejectReason = "未偵測到任何 SQL 陳述式。"
            };
        }

        foreach (var statement in statements)
        {
            var reason = CheckStatement(statement);
            if (reason != null)
                return new SqlReadOnlyValidationResult { IsValid = false, RejectReason = reason };
        }

        return new SqlReadOnlyValidationResult { IsValid = true };
    }

    private static string? CheckStatement(TSqlStatement statement) => statement switch
    {
        SelectStatement { Into: not null } =>
            "SELECT ... INTO 會建立資料表，查詢僅支援唯讀操作。",
        SelectStatement => null,
        DeclareVariableStatement => null,
        SetVariableStatement => null,
        PredicateSetStatement => null,
        SetTransactionIsolationLevelStatement => null,
        _ =>
            $"查詢僅支援 SELECT 等唯讀操作（偵測到 {DescribeStatement(statement)}）；" +
            "資料異動請改用 DML 執行通道（dry run 預演／execute 執行）。"
    };

    private static string DescribeStatement(TSqlStatement statement)
        => statement.GetType().Name.Replace("Statement", "");
}
