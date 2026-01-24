namespace TableSpec.Domain.Enums;

/// <summary>
/// 同步動作
/// </summary>
public enum SyncAction
{
    /// <summary>
    /// 略過 - 不處理此差異
    /// </summary>
    Skip = 0,

    /// <summary>
    /// 執行 - 直接執行同步
    /// </summary>
    Execute = 1,

    /// <summary>
    /// 僅匯出腳本 - 不直接執行，只產生腳本
    /// </summary>
    ExportScriptOnly = 2
}
