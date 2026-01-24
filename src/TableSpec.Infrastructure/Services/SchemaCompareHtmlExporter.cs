using System.Text;
using System.Web;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;

namespace TableSpec.Infrastructure.Services;

/// <summary>
/// Schema Compare HTML 匯出器
/// </summary>
public class SchemaCompareHtmlExporter
{
    /// <summary>
    /// 匯出比對結果到 HTML 檔案
    /// </summary>
    public async Task ExportAsync(IEnumerable<SchemaComparison> comparisons, string filePath)
    {
        var comparisonList = comparisons.ToList();
        var html = GenerateHtml(comparisonList);
        await File.WriteAllTextAsync(filePath, html, Encoding.UTF8);
    }

    /// <summary>
    /// 產生 HTML 字串（可用於內嵌預覽）
    /// </summary>
    public string GenerateHtml(IList<SchemaComparison> comparisons)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-TW\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("    <title>Schema Compare 報告</title>");
        sb.AppendLine(GenerateCss());
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // 標題
        sb.AppendLine("    <div class=\"container\">");
        sb.AppendLine("        <h1>Schema Compare 報告</h1>");
        sb.AppendLine($"        <p class=\"timestamp\">產生時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");

        if (comparisons.Count > 0)
        {
            sb.AppendLine($"        <p><strong>基準環境：</strong>{Encode(comparisons[0].BaseEnvironment)}</p>");
        }

        // 摘要區塊
        sb.AppendLine(GenerateSummarySection(comparisons));

        // 各環境差異詳情
        foreach (var comparison in comparisons)
        {
            sb.AppendLine(GenerateComparisonSection(comparison));
        }

        sb.AppendLine("    </div>");
        sb.AppendLine(GenerateJavaScript());
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    #region CSS 樣式

    private static string GenerateCss()
    {
        return @"
    <style>
        * {
            box-sizing: border-box;
        }
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            background-color: #f5f5f5;
            margin: 0;
            padding: 20px;
        }
        .container {
            max-width: 1200px;
            margin: 0 auto;
            background: white;
            padding: 30px;
            border-radius: 8px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
        }
        h1 {
            color: #2c3e50;
            border-bottom: 3px solid #3498db;
            padding-bottom: 10px;
        }
        h2 {
            color: #34495e;
            margin-top: 30px;
        }
        .timestamp {
            color: #7f8c8d;
            font-size: 0.9em;
        }
        .summary-table {
            width: 100%;
            border-collapse: collapse;
            margin: 20px 0;
        }
        .summary-table th, .summary-table td {
            padding: 12px;
            text-align: left;
            border: 1px solid #ddd;
        }
        .summary-table th {
            background-color: #3498db;
            color: white;
        }
        .summary-table tr:nth-child(even) {
            background-color: #f9f9f9;
        }
        .collapsible {
            background-color: #ecf0f1;
            color: #2c3e50;
            cursor: pointer;
            padding: 15px;
            width: 100%;
            border: none;
            text-align: left;
            outline: none;
            font-size: 16px;
            font-weight: bold;
            border-radius: 4px;
            margin-top: 10px;
            transition: background-color 0.3s;
        }
        .collapsible:hover {
            background-color: #bdc3c7;
        }
        .collapsible:after {
            content: '▼';
            float: right;
            transition: transform 0.3s;
        }
        .collapsible.active:after {
            transform: rotate(180deg);
        }
        .content {
            padding: 0 18px;
            max-height: 0;
            overflow: hidden;
            transition: max-height 0.3s ease-out;
            background-color: white;
            border: 1px solid #ddd;
            border-top: none;
            border-radius: 0 0 4px 4px;
        }
        .diff-table {
            width: 100%;
            border-collapse: collapse;
            margin: 15px 0;
        }
        .diff-table th, .diff-table td {
            padding: 10px;
            text-align: left;
            border: 1px solid #ddd;
            font-size: 14px;
        }
        .diff-table th {
            background-color: #34495e;
            color: white;
        }
        .risk-low {
            background-color: #2ecc71;
            color: white;
            padding: 3px 8px;
            border-radius: 3px;
            font-weight: bold;
        }
        .risk-medium {
            background-color: #f39c12;
            color: white;
            padding: 3px 8px;
            border-radius: 3px;
            font-weight: bold;
        }
        .risk-high {
            background-color: #e74c3c;
            color: white;
            padding: 3px 8px;
            border-radius: 3px;
            font-weight: bold;
        }
        .risk-forbidden {
            background-color: #2c3e50;
            color: white;
            padding: 3px 8px;
            border-radius: 3px;
            font-weight: bold;
        }
        .badge {
            display: inline-block;
            padding: 3px 10px;
            border-radius: 12px;
            font-size: 12px;
            font-weight: bold;
            margin-left: 10px;
        }
        .badge-count {
            background-color: #3498db;
            color: white;
        }
        .no-diff {
            color: #27ae60;
            font-style: italic;
        }
    </style>";
    }

    #endregion

    #region JavaScript

    private static string GenerateJavaScript()
    {
        return @"
    <script>
        document.addEventListener('DOMContentLoaded', function() {
            var coll = document.getElementsByClassName('collapsible');
            for (var i = 0; i < coll.length; i++) {
                coll[i].addEventListener('click', function() {
                    this.classList.toggle('active');
                    var content = this.nextElementSibling;
                    if (content.style.maxHeight) {
                        content.style.maxHeight = null;
                    } else {
                        content.style.maxHeight = content.scrollHeight + 'px';
                    }
                });
            }

            // 預設展開第一個
            if (coll.length > 0) {
                coll[0].click();
            }
        });
    </script>";
    }

    #endregion

    #region 摘要區塊

    private string GenerateSummarySection(IList<SchemaComparison> comparisons)
    {
        var sb = new StringBuilder();

        sb.AppendLine("        <h2>摘要</h2>");
        sb.AppendLine("        <table class=\"summary-table\">");
        sb.AppendLine("            <thead>");
        sb.AppendLine("                <tr>");
        sb.AppendLine("                    <th>目標環境</th>");
        sb.AppendLine("                    <th>差異數量</th>");
        sb.AppendLine("                    <th>低風險</th>");
        sb.AppendLine("                    <th>中風險</th>");
        sb.AppendLine("                    <th>高風險</th>");
        sb.AppendLine("                    <th>禁止</th>");
        sb.AppendLine("                </tr>");
        sb.AppendLine("            </thead>");
        sb.AppendLine("            <tbody>");

        foreach (var comparison in comparisons)
        {
            var summary = comparison.RiskSummary;
            sb.AppendLine("                <tr>");
            sb.AppendLine($"                    <td>{Encode(comparison.TargetEnvironment)}</td>");
            sb.AppendLine($"                    <td><strong>{summary.TotalCount}</strong></td>");
            sb.AppendLine($"                    <td><span class=\"risk-low\">{summary.LowCount}</span></td>");
            sb.AppendLine($"                    <td><span class=\"risk-medium\">{summary.MediumCount}</span></td>");
            sb.AppendLine($"                    <td><span class=\"risk-high\">{summary.HighCount}</span></td>");
            sb.AppendLine($"                    <td><span class=\"risk-forbidden\">{summary.ForbiddenCount}</span></td>");
            sb.AppendLine("                </tr>");
        }

        sb.AppendLine("            </tbody>");
        sb.AppendLine("        </table>");

        return sb.ToString();
    }

    #endregion

    #region 差異詳情區塊

    private string GenerateComparisonSection(SchemaComparison comparison)
    {
        var sb = new StringBuilder();
        var diffCount = comparison.Differences.Count;

        sb.AppendLine($"        <button class=\"collapsible\">");
        sb.AppendLine($"            {Encode(comparison.TargetEnvironment)}");
        sb.AppendLine($"            <span class=\"badge badge-count\">{diffCount} 個差異</span>");
        sb.AppendLine("        </button>");
        sb.AppendLine("        <div class=\"content\">");

        if (diffCount == 0)
        {
            sb.AppendLine("            <p class=\"no-diff\">✓ 沒有差異，與基準環境完全相同</p>");
        }
        else
        {
            // 按物件類型分組
            var groupedDiffs = comparison.Differences
                .GroupBy(d => d.ObjectType)
                .OrderBy(g => g.Key);

            foreach (var group in groupedDiffs)
            {
                sb.AppendLine($"            <h3>{GetObjectTypeDisplayName(group.Key)} ({group.Count()})</h3>");
                sb.AppendLine("            <table class=\"diff-table\">");
                sb.AppendLine("                <thead>");
                sb.AppendLine("                    <tr>");
                sb.AppendLine("                        <th>物件名稱</th>");
                sb.AppendLine("                        <th>差異類型</th>");
                sb.AppendLine("                        <th>風險等級</th>");
                sb.AppendLine("                        <th>描述</th>");
                sb.AppendLine("                    </tr>");
                sb.AppendLine("                </thead>");
                sb.AppendLine("                <tbody>");

                foreach (var diff in group)
                {
                    var riskClass = GetRiskLevelCssClass(diff.RiskLevel);
                    sb.AppendLine("                    <tr>");
                    sb.AppendLine($"                        <td><code>{Encode(diff.ObjectName)}</code></td>");
                    sb.AppendLine($"                        <td>{GetDifferenceTypeDisplayName(diff.DifferenceType)}</td>");
                    sb.AppendLine($"                        <td><span class=\"{riskClass}\">{GetRiskLevelDisplayName(diff.RiskLevel)}</span></td>");
                    sb.AppendLine($"                        <td>{Encode(diff.Description ?? "")}</td>");
                    sb.AppendLine("                    </tr>");
                }

                sb.AppendLine("                </tbody>");
                sb.AppendLine("            </table>");
            }
        }

        sb.AppendLine("        </div>");

        return sb.ToString();
    }

    #endregion

    #region 輔助方法

    private static string Encode(string text)
    {
        return HttpUtility.HtmlEncode(text);
    }

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

    private static string GetRiskLevelCssClass(RiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RiskLevel.Low => "risk-low",
            RiskLevel.Medium => "risk-medium",
            RiskLevel.High => "risk-high",
            RiskLevel.Forbidden => "risk-forbidden",
            _ => ""
        };
    }

    #endregion
}
