using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TableSpec.Application.Services;

namespace TableSpec.McpServer.Tools;

/// <summary>
/// 資料表查詢 MCP 工具
/// </summary>
[McpServerToolType]
public static class TableTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 列出所有資料庫物件
    /// </summary>
    [McpServerTool, Description("列出目前資料庫的所有物件（資料表、檢視、預存程序、函數），可選擇依類型篩選")]
    public static async Task<string> ListTables(
        ITableQueryService tableQueryService,
        [Description("物件類型篩選（可選）：BASE TABLE、VIEW、PROCEDURE、FUNCTION。留空則列出全部")] string? type = null)
    {
        var tables = string.IsNullOrEmpty(type)
            ? await tableQueryService.GetAllTablesAsync()
            : await tableQueryService.GetTablesByTypeAsync(type);

        if (tables.Count == 0)
            return "目前資料庫中沒有物件，或尚未連線至資料庫。";

        var result = tables.Select(t => new
        {
            t.Type,
            t.Schema,
            t.Name,
            t.Description
        });

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    /// <summary>
    /// 取得指定物件的欄位資訊
    /// </summary>
    [McpServerTool, Description("取得指定資料表或檢視的欄位資訊，包含資料型別、是否可 Null、主鍵、描述等")]
    public static async Task<string> GetColumns(
        ITableQueryService tableQueryService,
        [Description("Schema 名稱，例如 dbo")] string schema,
        [Description("物件名稱（資料表或檢視名稱）")] string tableName,
        [Description("物件類型：BASE TABLE、VIEW、PROCEDURE、FUNCTION（預設 BASE TABLE）")] string type = "BASE TABLE")
    {
        var columns = await tableQueryService.GetColumnsAsync(type, schema, tableName);

        if (columns.Count == 0)
            return $"找不到 [{schema}].[{tableName}] 的欄位資訊。";

        var result = columns.Select(c => new
        {
            c.ColumnName,
            c.DataType,
            c.Length,
            c.IsNullable,
            c.IsPrimaryKey,
            c.IsUniqueKey,
            c.IsIndexed,
            c.DefaultValue,
            c.Description
        });

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    /// <summary>
    /// 取得指定表格的索引資訊
    /// </summary>
    [McpServerTool, Description("取得指定資料表的索引資訊，包含索引名稱、類型、欄位、是否唯一等")]
    public static async Task<string> GetIndexes(
        ITableQueryService tableQueryService,
        [Description("Schema 名稱，例如 dbo")] string schema,
        [Description("資料表名稱")] string tableName)
    {
        var indexes = await tableQueryService.GetIndexesAsync(schema, tableName);

        if (indexes.Count == 0)
            return $"[{schema}].[{tableName}] 沒有索引。";

        var result = indexes.Select(i => new
        {
            i.Name,
            i.Type,
            i.Columns,
            i.IsUnique,
            i.IsPrimaryKey
        });

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    /// <summary>
    /// 取得指定表格的外鍵關聯
    /// </summary>
    [McpServerTool, Description("取得指定資料表的外鍵關聯，包含參考的資料表和欄位")]
    public static async Task<string> GetRelations(
        ITableQueryService tableQueryService,
        [Description("Schema 名稱，例如 dbo")] string schema,
        [Description("資料表名稱")] string tableName)
    {
        var relations = await tableQueryService.GetRelationsAsync(schema, tableName);

        if (relations.Count == 0)
            return $"[{schema}].[{tableName}] 沒有外鍵關聯。";

        return JsonSerializer.Serialize(relations, JsonOptions);
    }

    /// <summary>
    /// 取得預存程序或函數的參數
    /// </summary>
    [McpServerTool, Description("取得預存程序或函數的參數資訊")]
    public static async Task<string> GetParameters(
        ITableQueryService tableQueryService,
        [Description("Schema 名稱，例如 dbo")] string schema,
        [Description("預存程序或函數名稱")] string objectName)
    {
        var parameters = await tableQueryService.GetParametersAsync(schema, objectName);

        if (parameters.Count == 0)
            return $"[{schema}].[{objectName}] 沒有參數。";

        return JsonSerializer.Serialize(parameters, JsonOptions);
    }

    /// <summary>
    /// 取得預存程序或函數的 SQL 定義
    /// </summary>
    [McpServerTool, Description("取得預存程序或函數的完整 SQL 定義（原始碼）")]
    public static async Task<string> GetDefinition(
        ITableQueryService tableQueryService,
        [Description("Schema 名稱，例如 dbo")] string schema,
        [Description("預存程序或函數名稱")] string objectName)
    {
        var definition = await tableQueryService.GetDefinitionAsync(schema, objectName);

        return definition ?? $"找不到 [{schema}].[{objectName}] 的定義，可能物件不存在或為加密物件。";
    }
}
