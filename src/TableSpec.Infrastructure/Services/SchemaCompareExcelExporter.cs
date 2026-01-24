using ClosedXML.Excel;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;

namespace TableSpec.Infrastructure.Services;

/// <summary>
/// Schema Compare Excel 匯出器
/// </summary>
public class SchemaCompareExcelExporter
{
    /// <summary>
    /// 匯出比對結果到 Excel 檔案
    /// </summary>
    public Task ExportAsync(IEnumerable<SchemaComparison> comparisons, string filePath)
    {
        var comparisonList = comparisons.ToList();

        using var workbook = new XLWorkbook();

        // 建立摘要工作表
        CreateSummarySheet(workbook, comparisonList);

        // 建立差異清單工作表
        CreateDifferenceSheet(workbook, comparisonList);

        // 儲存檔案
        workbook.SaveAs(filePath);

        return Task.CompletedTask;
    }

    #region 摘要工作表

    private void CreateSummarySheet(XLWorkbook workbook, IList<SchemaComparison> comparisons)
    {
        var sheet = workbook.Worksheets.Add("摘要");
        var row = 1;

        // 標題
        sheet.Cell(row, 1).Value = "Schema Compare 報告";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 16;
        row += 2;

        // 產生時間
        sheet.Cell(row, 1).Value = "產生時間：";
        sheet.Cell(row, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        row += 2;

        // 基準環境
        if (comparisons.Count > 0)
        {
            sheet.Cell(row, 1).Value = "基準環境：";
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = comparisons[0].BaseEnvironment;
            row += 2;
        }

        // 比對環境清單
        sheet.Cell(row, 1).Value = "比對環境";
        sheet.Cell(row, 2).Value = "差異數量";
        sheet.Cell(row, 3).Value = "低風險";
        sheet.Cell(row, 4).Value = "中風險";
        sheet.Cell(row, 5).Value = "高風險";
        sheet.Cell(row, 6).Value = "禁止";

        // 設定標題樣式
        var headerRange = sheet.Range(row, 1, row, 6);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        row++;

        // 填入各環境資料
        foreach (var comparison in comparisons)
        {
            var summary = comparison.RiskSummary;

            sheet.Cell(row, 1).Value = comparison.TargetEnvironment;
            sheet.Cell(row, 2).Value = summary.TotalCount;
            sheet.Cell(row, 3).Value = summary.LowCount;
            sheet.Cell(row, 4).Value = summary.MediumCount;
            sheet.Cell(row, 5).Value = summary.HighCount;
            sheet.Cell(row, 6).Value = summary.ForbiddenCount;

            // 根據風險數量設定顏色
            if (summary.ForbiddenCount > 0 || summary.HighCount > 0)
            {
                sheet.Cell(row, 2).Style.Fill.BackgroundColor = XLColor.LightCoral;
            }
            else if (summary.MediumCount > 0)
            {
                sheet.Cell(row, 2).Style.Fill.BackgroundColor = XLColor.LightYellow;
            }
            else if (summary.TotalCount > 0)
            {
                sheet.Cell(row, 2).Style.Fill.BackgroundColor = XLColor.LightGreen;
            }

            row++;
        }

        // 自動調整欄寬
        sheet.Columns().AdjustToContents();
    }

    #endregion

    #region 差異清單工作表

    private void CreateDifferenceSheet(XLWorkbook workbook, IList<SchemaComparison> comparisons)
    {
        var sheet = workbook.Worksheets.Add("差異清單");
        var row = 1;

        // 標題列
        var headers = new[] { "目標環境", "物件類型", "物件名稱", "差異類型", "風險等級", "屬性", "來源值", "目標值", "描述" };
        for (var col = 1; col <= headers.Length; col++)
        {
            sheet.Cell(row, col).Value = headers[col - 1];
        }

        // 設定標題樣式
        var headerRange = sheet.Range(row, 1, row, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        row++;

        // 填入差異資料
        foreach (var comparison in comparisons)
        {
            foreach (var diff in comparison.Differences)
            {
                sheet.Cell(row, 1).Value = comparison.TargetEnvironment;
                sheet.Cell(row, 2).Value = GetObjectTypeDisplayName(diff.ObjectType);
                sheet.Cell(row, 3).Value = diff.ObjectName;
                sheet.Cell(row, 4).Value = GetDifferenceTypeDisplayName(diff.DifferenceType);
                sheet.Cell(row, 5).Value = GetRiskLevelDisplayName(diff.RiskLevel);
                sheet.Cell(row, 6).Value = diff.PropertyName ?? "";
                sheet.Cell(row, 7).Value = diff.SourceValue ?? "";
                sheet.Cell(row, 8).Value = diff.TargetValue ?? "";
                sheet.Cell(row, 9).Value = diff.Description ?? "";

                // 設定風險等級顏色
                var riskCell = sheet.Cell(row, 5);
                riskCell.Style.Fill.BackgroundColor = GetRiskLevelColor(diff.RiskLevel);

                row++;
            }
        }

        // 自動調整欄寬
        sheet.Columns().AdjustToContents();

        // 設定篩選
        if (row > 1)
        {
            sheet.RangeUsed()?.SetAutoFilter();
        }
    }

    #endregion

    #region 輔助方法

    private static string GetObjectTypeDisplayName(SchemaObjectType objectType)
    {
        return objectType switch
        {
            SchemaObjectType.Table => "表格",
            SchemaObjectType.Column => "欄位",
            SchemaObjectType.Index => "索引",
            SchemaObjectType.Constraint => "約束",
            SchemaObjectType.View => "檢視表",
            SchemaObjectType.StoredProcedure => "預存程序",
            SchemaObjectType.Function => "函數",
            SchemaObjectType.Trigger => "觸發程序",
            _ => objectType.ToString()
        };
    }

    private static string GetDifferenceTypeDisplayName(DifferenceType differenceType)
    {
        return differenceType switch
        {
            DifferenceType.Added => "新增",
            DifferenceType.Modified => "修改",
            _ => differenceType.ToString()
        };
    }

    private static string GetRiskLevelDisplayName(RiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RiskLevel.Low => "低",
            RiskLevel.Medium => "中",
            RiskLevel.High => "高",
            RiskLevel.Forbidden => "禁止",
            _ => riskLevel.ToString()
        };
    }

    private static XLColor GetRiskLevelColor(RiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RiskLevel.Low => XLColor.LightGreen,
            RiskLevel.Medium => XLColor.LightYellow,
            RiskLevel.High => XLColor.LightCoral,
            RiskLevel.Forbidden => XLColor.DarkRed,
            _ => XLColor.White
        };
    }

    #endregion
}
