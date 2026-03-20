using System.ComponentModel;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 匯出功能 MCP 工具
/// </summary>
[McpServerToolType]
public static class ExportTools
{
    [McpServerTool, Description("匯出所有資料表規格為 Excel 檔案")]
    public static async Task<string> ExportAllToExcel(
        IExportService exportService,
        [Description("輸出檔案路徑（如 /tmp/tables.xlsx）")] string outputPath)
    {
        try
        {
            var data = await exportService.ExportToExcelAsync();
            await File.WriteAllBytesAsync(outputPath, data);
            return $"已匯出至 {outputPath}（{data.Length / 1024} KB）";
        }
        catch (Exception ex)
        {
            return $"匯出失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("匯出指定資料表規格為 Excel 檔案")]
    public static async Task<string> ExportTableToExcel(
        IExportService exportService,
        [Description("Schema 名稱（如 dbo）")] string schema,
        [Description("資料表名稱")] string tableName,
        [Description("輸出檔案路徑（如 /tmp/table.xlsx）")] string outputPath)
    {
        try
        {
            var data = await exportService.ExportTableToExcelAsync(schema, tableName);
            await File.WriteAllBytesAsync(outputPath, data);
            return $"已匯出 [{schema}].[{tableName}] 至 {outputPath}（{data.Length / 1024} KB）";
        }
        catch (Exception ex)
        {
            return $"匯出失敗：{ex.Message}";
        }
    }
}
