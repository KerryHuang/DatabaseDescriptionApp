using ClosedXML.Excel;
using TableSpec.Domain.Entities;

namespace TableSpec.Infrastructure.Services;

/// <summary>
/// 欄位使用統計 Excel 匯出服務
/// </summary>
public class ColumnUsageExcelExporter
{
    /// <summary>
    /// 匯出統計結果到 Excel
    /// </summary>
    /// <param name="filePath">檔案路徑</param>
    /// <param name="statistics">統計資料</param>
    /// <param name="ct">取消權杖</param>
    public async Task ExportAsync(
        string filePath,
        IReadOnlyList<ColumnUsageStatistics> statistics,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();

            // 工作表 1：統計摘要
            CreateSummarySheet(workbook, statistics);

            // 工作表 2：詳細明細
            CreateDetailSheet(workbook, statistics);

            workbook.SaveAs(filePath);
        }, ct);
    }

    /// <summary>
    /// 建立統計摘要工作表
    /// </summary>
    private static void CreateSummarySheet(XLWorkbook workbook, IReadOnlyList<ColumnUsageStatistics> statistics)
    {
        var sheet = workbook.Worksheets.Add("統計摘要");

        // 標題列
        sheet.Cell(1, 1).Value = "欄位名稱";
        sheet.Cell(1, 2).Value = "出現次數";
        sheet.Cell(1, 3).Value = "主要型別";
        sheet.Cell(1, 4).Value = "型別一致";
        sheet.Cell(1, 5).Value = "長度一致";
        sheet.Cell(1, 6).Value = "可空一致";

        // 設定標題樣式
        var headerRange = sheet.Range(1, 1, 1, 6);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

        // 資料列
        for (var i = 0; i < statistics.Count; i++)
        {
            var stat = statistics[i];
            var row = i + 2;

            sheet.Cell(row, 1).Value = stat.ColumnName;
            sheet.Cell(row, 2).Value = stat.UsageCount;
            sheet.Cell(row, 3).Value = stat.PrimaryDataType;
            sheet.Cell(row, 4).Value = stat.IsTypeConsistent ? "✓" : "✗";
            sheet.Cell(row, 5).Value = stat.IsLengthConsistent ? "✓" : "✗";
            sheet.Cell(row, 6).Value = stat.IsNullabilityConsistent ? "✓" : "✗";

            // 不一致的標記紅色
            if (!stat.IsTypeConsistent)
                sheet.Cell(row, 4).Style.Font.FontColor = XLColor.Red;
            if (!stat.IsLengthConsistent)
                sheet.Cell(row, 5).Style.Font.FontColor = XLColor.Red;
            if (!stat.IsNullabilityConsistent)
                sheet.Cell(row, 6).Style.Font.FontColor = XLColor.Red;
        }

        // 自動調整欄寬
        sheet.Columns().AdjustToContents();
    }

    /// <summary>
    /// 建立詳細明細工作表
    /// </summary>
    private static void CreateDetailSheet(XLWorkbook workbook, IReadOnlyList<ColumnUsageStatistics> statistics)
    {
        var sheet = workbook.Worksheets.Add("詳細明細");

        // 標題列
        sheet.Cell(1, 1).Value = "欄位名稱";
        sheet.Cell(1, 2).Value = "Schema";
        sheet.Cell(1, 3).Value = "物件名稱";
        sheet.Cell(1, 4).Value = "物件類型";
        sheet.Cell(1, 5).Value = "資料型別";
        sheet.Cell(1, 6).Value = "可空";
        sheet.Cell(1, 7).Value = "有差異";

        // 設定標題樣式
        var headerRange = sheet.Range(1, 1, 1, 7);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

        // 資料列
        var row = 2;
        foreach (var stat in statistics)
        {
            foreach (var usage in stat.Usages)
            {
                sheet.Cell(row, 1).Value = stat.ColumnName;
                sheet.Cell(row, 2).Value = usage.SchemaName;
                sheet.Cell(row, 3).Value = usage.ObjectName;
                sheet.Cell(row, 4).Value = usage.ObjectType;
                sheet.Cell(row, 5).Value = usage.DataType;
                sheet.Cell(row, 6).Value = usage.IsNullable ? "NULL" : "NOT NULL";
                sheet.Cell(row, 7).Value = usage.HasDifference ? "✗" : "";

                // 有差異的標記紅色
                if (usage.HasDifference)
                {
                    sheet.Cell(row, 7).Style.Font.FontColor = XLColor.Red;
                    sheet.Row(row).Style.Fill.BackgroundColor = XLColor.LightPink;
                }

                row++;
            }
        }

        // 自動調整欄寬
        sheet.Columns().AdjustToContents();
    }
}
