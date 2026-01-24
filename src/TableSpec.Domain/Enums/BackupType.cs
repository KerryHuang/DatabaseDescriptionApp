namespace TableSpec.Domain.Enums;

/// <summary>
/// 備份類型
/// </summary>
public enum BackupType
{
    /// <summary>完整備份</summary>
    Full,

    /// <summary>差異備份</summary>
    Differential,

    /// <summary>交易記錄備份</summary>
    TransactionLog
}
