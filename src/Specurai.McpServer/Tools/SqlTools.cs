using System.ComponentModel;
using System.Data;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;
using Specurai.Domain.Interfaces;

namespace Specurai.McpServer.Tools;

/// <summary>
/// SQL 查詢 MCP 工具
/// </summary>
[McpServerToolType]
public static class SqlTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 執行唯讀 SQL 查詢
    /// </summary>
    [McpServerTool, Description("執行唯讀 SQL 查詢並返回結果。僅支援 SELECT 等讀取操作")]
    public static async Task<string> ExecuteReadonlySql(
        ISqlQueryRepository sqlQueryRepository,
        [Description("要執行的 SQL 查詢語句（僅限 SELECT 等唯讀操作）")] string sql)
    {
        // 基本的安全檢查：阻擋明顯的寫入操作
        var normalizedSql = sql.Trim().ToUpperInvariant();
        var dangerousKeywords = new[] { "INSERT ", "UPDATE ", "DELETE ", "DROP ", "ALTER ", "CREATE ", "TRUNCATE ", "EXEC ", "EXECUTE " };

        foreach (var keyword in dangerousKeywords)
        {
            if (normalizedSql.StartsWith(keyword, StringComparison.Ordinal))
                return $"安全限制：不允許執行 {keyword.Trim()} 操作。此工具僅支援唯讀查詢。";
        }

        try
        {
            var dataTable = await sqlQueryRepository.ExecuteQueryAsync(sql);
            return DataTableToJson(dataTable);
        }
        catch (Exception ex)
        {
            return $"查詢執行失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 搜尋欄位名稱
    /// </summary>
    [McpServerTool, Description("在目前資料庫中搜尋欄位名稱，支援模糊比對和精確比對")]
    public static async Task<string> SearchColumns(
        ISqlQueryRepository sqlQueryRepository,
        [Description("欄位名稱關鍵字")] string columnName,
        [Description("是否精確比對（預設 false 為模糊搜尋）")] bool exactMatch = false)
    {
        var results = await sqlQueryRepository.SearchColumnsAsync(columnName, exactMatch);

        if (results.Count == 0)
            return $"找不到符合「{columnName}」的欄位。";

        var formatted = results.Select(r => new
        {
            r.SchemaName,
            r.ObjectName,
            r.ObjectType,
            r.ColumnName,
            r.DataType,
            r.Description
        });

        return JsonSerializer.Serialize(formatted, JsonOptions);
    }

    /// <summary>
    /// 取得建表語句
    /// </summary>
    [McpServerTool, Description("產生指定資料表的 CREATE TABLE SQL 語句")]
    public static async Task<string> GetCreateTableSql(
        ITableQueryService tableQueryService,
        [Description("Schema 名稱，例如 dbo")] string schema,
        [Description("資料表名稱")] string tableName)
    {
        try
        {
            var script = await tableQueryService.GetCreateTableSqlAsync(schema, tableName);
            return script ?? $"找不到資料表 [{schema}].[{tableName}]。";
        }
        catch (Exception ex)
        {
            return $"產生建表語句失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// Dry Run 預演單一 DML（一律回滾）
    /// </summary>
    [McpServerTool, Description("以 Dry Run 預演單一 DML（INSERT/UPDATE/DELETE）：驗證語法、在交易中執行以取得影響筆數與前後資料對照，最後一律 ROLLBACK，絕不修改資料")]
    public static async Task<string> DryRunSql(
        ISqlDryRunRepository sqlDryRunRepository,
        [Description("要預演的單一 DML 陳述式（INSERT/UPDATE/DELETE）")] string sql)
    {
        try
        {
            var result = await sqlDryRunRepository.DryRunAsync(sql);

            if (!result.IsValid)
            {
                return JsonSerializer.Serialize(new
                {
                    Valid = false,
                    result.RejectReason,
                    SyntaxErrors = result.SyntaxErrors.Select(e => new { e.Line, e.Column, e.Message }),
                    DatabaseChanged = false
                }, JsonOptions);
            }

            if (result.ExecutionError != null)
            {
                return JsonSerializer.Serialize(new
                {
                    Valid = true,
                    StatementType = result.StatementType.ToString(),
                    result.ExecutionError,
                    result.Warnings,
                    RolledBack = true,
                    DatabaseChanged = false
                }, JsonOptions);
            }

            return JsonSerializer.Serialize(new
            {
                Valid = true,
                StatementType = result.StatementType.ToString(),
                result.AffectedRowCount,
                PreviewColumns = result.PreviewTable?.Columns.Cast<DataColumn>()
                    .Select(c => c.ColumnName).ToArray(),
                PreviewRows = result.PreviewTable == null ? null : DataTableToRows(result.PreviewTable),
                result.PreviewTruncated,
                result.Warnings,
                RolledBack = true,
                DatabaseChanged = false
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"Dry run 執行失敗：{ex.Message}";
        }
    }

    private static List<Dictionary<string, object?>> DataTableToRows(DataTable dataTable)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (DataRow row in dataTable.Rows)
        {
            var dict = new Dictionary<string, object?>();
            foreach (DataColumn col in dataTable.Columns)
            {
                dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
            }
            rows.Add(dict);
        }
        return rows;
    }

    private static string DataTableToJson(DataTable dataTable)
    {
        var rows = DataTableToRows(dataTable);

        var result = new
        {
            RowCount = rows.Count,
            Columns = dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray(),
            Rows = rows
        };

        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
