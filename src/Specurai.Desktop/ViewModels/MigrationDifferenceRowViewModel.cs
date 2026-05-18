using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Desktop.ViewModels;

/// <summary>目標列數的量級分類。</summary>
public enum RowCountBucket
{
    None = 0,    // 無資料（程式物件）
    Low = 1,     // 🟢 < 10 萬
    Medium = 2,  // 🟡 10 萬 ~ 100 萬
    High = 3     // 🔴 ≥ 100 萬
}

/// <summary>
/// Migration 差異表格每列 ViewModel
/// </summary>
public partial class MigrationDifferenceRowViewModel : ViewModelBase
{
    public SchemaDifference Difference { get; }

    [ObservableProperty]
    private bool _isSelected;

    public event Action<bool>? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        // 任何勾選變更都通知（包含高風險用於預覽 SQL）；接收端依 IsExecutable 區分計數
        SelectionChanged?.Invoke(value);
    }

    public bool IsExecutable => Difference.RiskLevel < RiskLevel.High;

    /// <summary>勾選框 ToolTip：高風險僅可用於預覽 / 下載 SQL，不會被 Dry Run 或 Migration 執行。</summary>
    public string SelectionTooltip => IsExecutable
        ? "勾選後此項目會被 Dry Run 與執行 Migration 處理"
        : "高風險：可勾選用於預覽 SQL / 下載 SQL，但 Dry Run 與執行 Migration 仍會跳過";

    public string RiskLevelText => Difference.RiskLevel switch
    {
        RiskLevel.Low => "🟢 低風險",
        RiskLevel.Medium => "🟡 中風險",
        RiskLevel.High => "🔴 高風險",
        RiskLevel.Forbidden => "🔴 禁止",
        _ => "未知"
    };

    public string SchemaText => Difference.Schema;

    public string ObjectTypeText => Difference.ObjectType switch
    {
        SchemaObjectType.Table => "表格",
        SchemaObjectType.Column => "欄位",
        SchemaObjectType.Index => "索引",
        SchemaObjectType.Constraint => "約束",
        SchemaObjectType.View => "檢視表",
        SchemaObjectType.StoredProcedure => "預存程序",
        SchemaObjectType.Function => "函數",
        SchemaObjectType.Trigger => "觸發程序",
        _ => Difference.ObjectType.ToString()
    };

    public string ObjectTypeDescription => Difference.ObjectType switch
    {
        SchemaObjectType.Table => "表格（Table）：資料庫主要資料儲存結構，包含欄位、主鍵等定義",
        SchemaObjectType.Column => "欄位（Column）：表格中的單一資料行，定義型別、是否可 NULL、預設值等",
        SchemaObjectType.Index => "索引（Index）：加速查詢的資料結構，包含 Clustered / Non-clustered / Unique 等類型",
        SchemaObjectType.Constraint => "約束（Constraint）：資料完整性規則，包含 PK（主鍵）、FK（外鍵）、Unique、Check、Default",
        SchemaObjectType.View => "檢視表（View）：以 SQL 查詢定義的虛擬表格，不儲存實際資料",
        SchemaObjectType.StoredProcedure => "預存程序（Stored Procedure）：伺服器端可執行的 T-SQL 程式邏輯，支援參數傳入",
        SchemaObjectType.Function => "函數（Function）：回傳單一值或資料表的可重用邏輯（Scalar / Table-valued）",
        SchemaObjectType.Trigger => "觸發程序（Trigger）：在 INSERT / UPDATE / DELETE 事件發生時自動執行的程式碼",
        _ => Difference.ObjectType.ToString()
    };

    public string DifferenceTypeText => Difference.DifferenceType switch
    {
        DifferenceType.Added => "新增",
        DifferenceType.Modified => Difference.PropertyName ?? "修改",
        _ => Difference.DifferenceType.ToString()
    };

    /// <summary>目標資料庫該物件所屬資料表的概估列數（CREATE 新表 / 程式物件為 null）。</summary>
    public long? TargetRowCount { get; init; }

    /// <summary>列數顯示文字（含千分位）；無資料顯示「—」。</summary>
    public string RowCountText => TargetRowCount is null
        ? "—"
        : TargetRowCount.Value.ToString("N0");

    /// <summary>列數警示圖示：≥100 萬 🔴；≥10 萬 🟡；其餘空白。</summary>
    public string RowCountBadge => TargetRowCount switch
    {
        null => string.Empty,
        >= 1_000_000 => "🔴",
        >= 100_000 => "🟡",
        _ => "🟢"
    };

    /// <summary>列數量級分類，用於篩選器。</summary>
    public RowCountBucket RowCountBucket => TargetRowCount switch
    {
        null => RowCountBucket.None,
        >= 1_000_000 => RowCountBucket.High,
        >= 100_000 => RowCountBucket.Medium,
        _ => RowCountBucket.Low
    };

    /// <summary>列數警示 ToolTip，提示 Migration 耗時風險。</summary>
    public string RowCountTooltip => TargetRowCount switch
    {
        null => "新增物件或程式物件，目標表尚無資料",
        >= 1_000_000 => $"目標表約 {TargetRowCount:N0} 列（大量資料）：ALTER / ADD COLUMN / CREATE INDEX 可能耗時數分鐘到數十分鐘，建議離峰執行並先備份",
        >= 100_000 => $"目標表約 {TargetRowCount:N0} 列：DDL 可能耗時數十秒到數分鐘",
        _ => $"目標表約 {TargetRowCount:N0} 列"
    };

    public MigrationDifferenceRowViewModel(SchemaDifference difference)
    {
        Difference = difference;
        // 預設全勾：可執行項目走 Dry Run/Migration；高風險項目供預覽/下載 SQL 給 DBA 人工處理
        _isSelected = true;
    }
}
