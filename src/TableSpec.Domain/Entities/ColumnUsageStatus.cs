namespace TableSpec.Domain.Entities;

/// <summary>
/// 欄位使用狀態資訊
/// </summary>
public class ColumnUsageStatus
{
    /// <summary>Schema 名稱</summary>
    public required string SchemaName { get; init; }

    /// <summary>資料表名稱</summary>
    public required string TableName { get; init; }

    /// <summary>欄位名稱</summary>
    public required string ColumnName { get; init; }

    /// <summary>資料型別</summary>
    public required string DataType { get; init; }

    /// <summary>是否被查詢計畫引用</summary>
    public bool IsReferencedInQueries { get; init; }

    /// <summary>是否全部為 NULL</summary>
    public bool IsAllNull { get; init; }

    /// <summary>是否所有非 NULL 值皆等於預設值</summary>
    public bool IsAllDefault { get; init; }

    /// <summary>欄位預設值</summary>
    public string? DefaultValue { get; init; }

    /// <summary>是否為主鍵</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>是否為外鍵</summary>
    public bool IsForeignKey { get; init; }

    /// <summary>是否為 Identity</summary>
    public bool IsIdentity { get; init; }

    /// <summary>
    /// 是否被使用。
    /// PK/FK/Identity 欄位一律視為使用中；
    /// 其餘欄位需被查詢引用，或含有非 NULL 且非預設值的資料。
    /// </summary>
    public bool IsUsed =>
        IsPrimaryKey || IsForeignKey || IsIdentity
        || IsReferencedInQueries
        || (!IsAllNull && !IsAllDefault);

    /// <summary>DROP COLUMN 語法</summary>
    public string DropColumnStatement =>
        $"ALTER TABLE [{SchemaName}].[{TableName}] DROP COLUMN [{ColumnName}]";
}
