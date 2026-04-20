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
