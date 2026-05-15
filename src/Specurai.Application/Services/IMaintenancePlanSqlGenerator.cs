using Specurai.Application.Models;
using Specurai.Domain.Entities;
using Specurai.Domain.Enums;

namespace Specurai.Application.Services;

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

    /// <summary>
    /// 產生可匯出的 SQL 腳本（含變數宣告和說明，方便使用者修改後執行）
    /// </summary>
    /// <param name="config">維護計劃設定</param>
    /// <param name="checkResults">步驟檢查結果清單</param>
    string GenerateExportSql(MaintenancePlanConfig config, IReadOnlyList<StepCheckResult> checkResults);

    /// <summary>產生 AdjustAutoGrowth 的 SQL（對所有檔案套用 MODIFY FILE FILEGROWTH）</summary>
    string GenerateAdjustAutoGrowthSql(MaintenancePlanConfig config, IReadOnlyList<DatabaseFileInfo> files);

    /// <summary>產生 PreExpandDataFile 的 SQL（對每個資料檔擴到「目前大小+bufferGB」湊整 GB）</summary>
    string GeneratePreExpandDataFileSql(MaintenancePlanConfig config, IReadOnlyList<DatabaseFileInfo> dataFiles);

    /// <summary>產生 CreateCheckDbJob 的 SQL</summary>
    string GenerateCreateCheckDbJobSql(MaintenancePlanConfig config, string? action = null);
}
