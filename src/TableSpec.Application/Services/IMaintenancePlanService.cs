using TableSpec.Application.Models;
using TableSpec.Domain.Entities;

namespace TableSpec.Application.Services;

/// <summary>
/// 維護計劃服務介面
/// </summary>
public interface IMaintenancePlanService
{
    /// <summary>
    /// 檢查執行維護計劃的前置條件
    /// </summary>
    /// <returns>是否就緒及錯誤訊息</returns>
    Task<(bool IsReady, string? ErrorMessage)> CheckPrerequisitesAsync(CancellationToken ct = default);

    /// <summary>
    /// 檢查各步驟目前狀態
    /// </summary>
    /// <param name="config">維護計劃設定</param>
    /// <param name="ct">取消權杖</param>
    Task<IReadOnlyList<StepCheckResult>> CheckStepsAsync(MaintenancePlanConfig config, CancellationToken ct = default);

    /// <summary>
    /// 執行維護計劃
    /// </summary>
    /// <param name="config">維護計劃設定</param>
    /// <param name="checkResults">步驟檢查結果清單</param>
    /// <param name="progress">進度回報</param>
    /// <param name="ct">取消權杖</param>
    Task ExecutePlanAsync(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults, IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 產生預覽 SQL
    /// </summary>
    /// <param name="config">維護計劃設定</param>
    /// <param name="checkResults">步驟檢查結果清單</param>
    Task<string> GeneratePreviewSqlAsync(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults);
}
