namespace TableSpec.Domain.Entities;

/// <summary>
/// 約束類型
/// </summary>
public enum ConstraintType
{
    /// <summary>主鍵</summary>
    PrimaryKey = 0,
    /// <summary>外鍵</summary>
    ForeignKey = 1,
    /// <summary>唯一索引</summary>
    UniqueIndex = 2,
    /// <summary>非叢集索引</summary>
    NonClusteredIndex = 3
}

/// <summary>
/// 約束資訊
/// </summary>
public class ConstraintInfo
{
    /// <summary>
    /// 約束名稱
    /// </summary>
    public string ConstraintName { get; set; } = string.Empty;

    /// <summary>
    /// 約束類型
    /// </summary>
    public ConstraintType Type { get; set; }

    /// <summary>
    /// Schema 名稱
    /// </summary>
    public string SchemaName { get; set; } = string.Empty;

    /// <summary>
    /// 資料表名稱
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// 包含的欄位清單
    /// </summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>
    /// 被參考的 Schema（FK 專用）
    /// </summary>
    public string? ReferencedSchema { get; set; }

    /// <summary>
    /// 被參考的資料表（FK 專用）
    /// </summary>
    public string? ReferencedTable { get; set; }

    /// <summary>
    /// 被參考的欄位（FK 專用）
    /// </summary>
    public string? ReferencedColumn { get; set; }

    /// <summary>
    /// 是否為唯一索引（Index 專用）
    /// </summary>
    public bool IsUnique { get; set; }

    /// <summary>
    /// 是否為叢集索引（Index 專用）
    /// </summary>
    public bool IsClustered { get; set; }

    /// <summary>
    /// 刪除約束的 SQL
    /// </summary>
    public string DropSql { get; set; } = string.Empty;

    /// <summary>
    /// 建立約束的 SQL
    /// </summary>
    public string CreateSql { get; set; } = string.Empty;
}
