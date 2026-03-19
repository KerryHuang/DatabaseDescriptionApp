using System.ComponentModel;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 描述管理 MCP 工具
/// </summary>
[McpServerToolType]
public static class DescriptionTools
{
    /// <summary>
    /// 更新資料表或檢視表的描述
    /// </summary>
    [McpServerTool, Description("更新資料表、檢視、預存程序或函數的描述（MS_Description 擴充屬性）")]
    public static async Task<string> UpdateTableDescription(
        ITableQueryService tableQueryService,
        [Description("物件類型：BASE TABLE、VIEW、PROCEDURE、FUNCTION")] string type,
        [Description("Schema 名稱，例如 dbo")] string schema,
        [Description("物件名稱")] string objectName,
        [Description("新的描述文字（傳入空字串可清除描述）")] string description)
    {
        try
        {
            await tableQueryService.UpdateTableDescriptionAsync(type, schema, objectName, description);
            return string.IsNullOrEmpty(description)
                ? $"已清除 [{schema}].[{objectName}] 的描述。"
                : $"已更新 [{schema}].[{objectName}] 的描述為「{description}」。";
        }
        catch (Exception ex)
        {
            return $"更新描述失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 更新欄位的描述
    /// </summary>
    [McpServerTool, Description("更新資料表或檢視表中指定欄位的描述（MS_Description 擴充屬性）")]
    public static async Task<string> UpdateColumnDescription(
        ITableQueryService tableQueryService,
        [Description("Schema 名稱，例如 dbo")] string schema,
        [Description("資料表或檢視名稱")] string objectName,
        [Description("欄位名稱")] string columnName,
        [Description("新的描述文字（傳入空字串可清除描述）")] string description,
        [Description("物件類型：TABLE 或 VIEW（預設 TABLE）")] string objectType = "TABLE")
    {
        try
        {
            await tableQueryService.UpdateColumnDescriptionAsync(schema, objectName, columnName, description, objectType);
            return string.IsNullOrEmpty(description)
                ? $"已清除 [{schema}].[{objectName}].[{columnName}] 的描述。"
                : $"已更新 [{schema}].[{objectName}].[{columnName}] 的描述為「{description}」。";
        }
        catch (Exception ex)
        {
            return $"更新欄位描述失敗：{ex.Message}";
        }
    }
}
