namespace Specurai.Domain.Enums;

/// <summary>
/// 維護計劃步驟
/// </summary>
public enum MaintenancePlanStep
{
    /// <summary>更新資料庫相容性層級至當前 SQL Server 版本</summary>
    SetCompatibilityLevel,
    /// <summary>設定 Recovery Model 為 SIMPLE</summary>
    SetRecoveryModel,
    /// <summary>重新命名邏輯檔名</summary>
    RenameLogicalFiles,
    /// <summary>建立登入帳號與使用者</summary>
    CreateLoginAndUser,
    /// <summary>將使用者加入 db_owner</summary>
    AddToDbOwner,
    /// <summary>建立每日全備份排程</summary>
    CreateBackupJob,
    /// <summary>建立每日還原排程</summary>
    CreateRestoreJob
}
