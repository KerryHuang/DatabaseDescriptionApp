using ClosedXML.Excel;
using TableSpec.Application.Services;

namespace TableSpec.Infrastructure.Services;

/// <summary>
/// Excel 匯出服務實作
/// </summary>
public class ExcelExportService : IExportService
{
    private readonly ITableQueryService _tableQueryService;

    public ExcelExportService(ITableQueryService tableQueryService)
    {
        _tableQueryService = tableQueryService;
    }

    public async Task<byte[]> ExportToExcelAsync(CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();

        var tables = await _tableQueryService.GetAllTablesAsync(ct);

        // 建立總表
        var summarySheet = workbook.Worksheets.Add("總表");
        summarySheet.Cell(1, 1).Value = "結構類型";
        summarySheet.Cell(1, 2).Value = "結構描述";
        summarySheet.Cell(1, 3).Value = "表格名稱";
        summarySheet.Cell(1, 4).Value = "表格名稱說明";

        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            var row = i + 2;

            summarySheet.Cell(row, 1).Value = table.Type;
            summarySheet.Cell(row, 2).Value = table.Schema;
            summarySheet.Cell(row, 3).Value = table.Name;
            summarySheet.Cell(row, 4).Value = table.Description;

            // 只為 Table 和 View 建立詳細頁
            if (table.Type is "BASE TABLE" or "VIEW")
            {
                try
                {
                    var sheetName = !string.IsNullOrEmpty(table.Description)
                        ? $"{table.Name} ({table.Description})"
                        : table.Name;

                    // 限制工作表名稱長度
                    if (sheetName.Length > 31)
                    {
                        sheetName = sheetName[..31];
                    }

                    // 移除無效字元
                    sheetName = sheetName.Replace(":", "_").Replace("/", "_").Replace("\\", "_")
                        .Replace("?", "_").Replace("*", "_").Replace("[", "_").Replace("]", "_");

                    var detailSheet = workbook.Worksheets.Add(sheetName);
                    await FillTableDetailSheet(detailSheet, table.Type, table.Schema, table.Name, ct);
                }
                catch
                {
                    // 忽略個別表格的錯誤，繼續處理下一個
                }
            }
        }

        // 設定總表樣式
        var summaryRange = summarySheet.Range(1, 1, tables.Count + 1, 4);
        summaryRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        summaryRange.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        summaryRange.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        summaryRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        summarySheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> ExportTableToExcelAsync(
        string schema,
        string tableName,
        CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();

        var detailSheet = workbook.Worksheets.Add(tableName);
        await FillTableDetailSheet(detailSheet, "BASE TABLE", schema, tableName, ct);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task FillTableDetailSheet(
        IXLWorksheet sheet,
        string type,
        string schema,
        string tableName,
        CancellationToken ct)
    {
        // 標題列
        sheet.Cell(1, 1).Value = "結構描述";
        sheet.Cell(1, 2).Value = "表格名稱";
        sheet.Cell(1, 3).Value = "欄位名稱";
        sheet.Cell(1, 4).Value = "資料型別";
        sheet.Cell(1, 5).Value = "長度";
        sheet.Cell(1, 6).Value = "預設值";
        sheet.Cell(1, 7).Value = "主鍵";
        sheet.Cell(1, 8).Value = "唯一索引";
        sheet.Cell(1, 9).Value = "索引";
        sheet.Cell(1, 10).Value = "允許空值";
        sheet.Cell(1, 11).Value = "欄位描述";

        var columns = await _tableQueryService.GetColumnsAsync(type, schema, tableName, ct);

        for (var i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            var row = i + 2;

            sheet.Cell(row, 1).Value = col.Schema;
            sheet.Cell(row, 2).Value = col.TableName;
            sheet.Cell(row, 3).Value = col.ColumnName;
            sheet.Cell(row, 4).Value = col.DataType;
            sheet.Cell(row, 5).Value = col.Length?.ToString() ?? "";
            sheet.Cell(row, 6).Value = col.DefaultValue ?? "";
            sheet.Cell(row, 7).Value = col.IsPrimaryKey ? "PK" : "";
            sheet.Cell(row, 8).Value = col.IsUniqueKey ? "UK" : "";
            sheet.Cell(row, 9).Value = col.IsIndexed ? "Indexed" : "";
            sheet.Cell(row, 10).Value = col.IsNullable ? "YES" : "NO";
            sheet.Cell(row, 11).Value = col.Description ?? "";
        }

        // 設定樣式
        var range = sheet.Range(1, 1, columns.Count + 1, 11);
        range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        range.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        range.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        sheet.Columns().AdjustToContents();
    }
}
