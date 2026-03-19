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
}
