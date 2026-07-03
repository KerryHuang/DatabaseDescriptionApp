using Specurai.Domain.Enums;

namespace Specurai.Application.Models;

/// <summary>
/// 維護計劃步驟檢查結果
/// </summary>
public class StepCheckResult
{
    /// <summary>步驟類型</summary>
    public required MaintenancePlanStep Step { get; init; }

    /// <summary>該步驟是否已經存在（不需再執行）</summary>
    public required bool AlreadyExists { get; init; }

    /// <summary>目前狀態描述</summary>
    public required string CurrentStatus { get; init; }

    /// <summary>可用的操作選項</summary>
    public required IReadOnlyList<string> AvailableActions { get; init; }

    /// <summary>使用者選擇的操作</summary>
    public string? SelectedAction { get; set; }

    /// <summary>步驟中文名稱</summary>
    public string StepName => Step switch
    {
        MaintenancePlanStep.SetCompatibilityLevel => "更新相容性層級",
        MaintenancePlanStep.SetRecoveryModel => "設定 Recovery Model",
        MaintenancePlanStep.AdjustAutoGrowth => "調整檔案成長設定",
        MaintenancePlanStep.PreExpandDataFile => "預擴資料檔",
        MaintenancePlanStep.RenameLogicalFiles => "重新命名邏輯檔名",
        MaintenancePlanStep.CreateLoginAndUser => "建立登入帳號與使用者",
        MaintenancePlanStep.AddToDbOwner => "加入 db_owner 角色",
        MaintenancePlanStep.CreateBackupJob => "建立備份排程",
        MaintenancePlanStep.CreateCheckDbJob => "建立完整性檢查排程",
        _ => Step.ToString()
    };
}
