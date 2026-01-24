namespace TableSpec.Domain.Enums;

/// <summary>
/// 還原模式
/// </summary>
public enum RestoreMode
{
    /// <summary>覆蓋原資料庫</summary>
    OverwriteExisting,

    /// <summary>還原到新資料庫</summary>
    CreateNew
}
