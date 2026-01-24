namespace TableSpec.Domain.Entities;

/// <summary>
/// 欄位型態資訊（含約束）
/// </summary>
public class ColumnTypeInfo
{
    /// <summary>
    /// 欄位名稱
    /// </summary>
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>
    /// Schema 名稱
    /// </summary>
    public string SchemaName { get; set; } = string.Empty;

    /// <summary>
    /// 資料表名稱
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// 完整資料型態（含長度，如 varchar(50)）
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// 基礎型態（如 varchar、nvarchar、int）
    /// </summary>
    public string BaseType { get; set; } = string.Empty;

    /// <summary>
    /// 最大長度
    /// </summary>
    public int MaxLength { get; set; }

    /// <summary>
    /// 精確度（decimal/numeric 專用）
    /// </summary>
    public int Precision { get; set; }

    /// <summary>
    /// 小數位數（decimal/numeric 專用）
    /// </summary>
    public int Scale { get; set; }

    /// <summary>
    /// 是否允許空值
    /// </summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// 欄位的約束清單
    /// </summary>
    public List<ConstraintInfo> Constraints { get; set; } = [];

    /// <summary>
    /// 完整的資料表名稱（Schema.TableName）
    /// </summary>
    public string FullTableName => $"{SchemaName}.{TableName}";

    /// <summary>
    /// 是否可以變更長度（僅支援字串型態）
    /// </summary>
    public bool IsLengthChangeable => BaseType.ToLowerInvariant() switch
    {
        "varchar" or "nvarchar" or "char" or "nchar" or "varbinary" or "binary" => true,
        _ => false
    };

    /// <summary>
    /// 是否與主要型態一致（由 ViewModel 設定）
    /// </summary>
    public bool IsConsistent { get; set; } = true;

    /// <summary>
    /// 狀態文字
    /// </summary>
    public string StatusText => IsConsistent ? "✓ 一致" : "✗ 不一致";
}
