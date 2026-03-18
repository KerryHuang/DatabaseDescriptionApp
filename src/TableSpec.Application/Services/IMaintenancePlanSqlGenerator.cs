using TableSpec.Application.Models;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;

namespace TableSpec.Application.Services;

/// <summary>
/// 維護計劃 SQL 產生器介面
/// </summary>
public interface IMaintenancePlanSqlGenerator
{
    /// <summary>
    /// 產生單一步驟的 SQL 指令
    /// </summary>
    /// <param name="step">步驟類型</param>
    /// <param name="config">維護計劃設定</param>
    /// <param name="action">使用者選擇的操作</param>
    string GenerateStepSql(MaintenancePlanStep step, MaintenancePlanConfig config, string? action = null);

    /// <summary>
    /// 產生完整維護計劃的 SQL 指令（含所有步驟）
    /// </summary>
    /// <param name="config">維護計劃設定</param>
    /// <param name="checkResults">步驟檢查結果清單</param>
    string GenerateFullSql(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults);
}
