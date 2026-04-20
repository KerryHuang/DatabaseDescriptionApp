using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;

namespace Specurai.Desktop.ViewModels;

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
        if (IsExecutable) SelectionChanged?.Invoke(value);
    }

    public bool IsExecutable => Difference.RiskLevel < RiskLevel.High;

    public string RiskLevelText => Difference.RiskLevel switch
    {
        RiskLevel.Low => "🟢 低風險",
        RiskLevel.Medium => "🟡 中風險",
        RiskLevel.High => "🔴 高風險",
        RiskLevel.Forbidden => "🔴 禁止",
        _ => "未知"
    };

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

    public MigrationDifferenceRowViewModel(SchemaDifference difference)
    {
        Difference = difference;
        _isSelected = IsExecutable;
    }
}
