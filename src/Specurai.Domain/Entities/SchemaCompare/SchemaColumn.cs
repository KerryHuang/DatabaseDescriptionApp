namespace Specurai.Domain.Entities.SchemaCompare;

/// <summary>
/// 欄位結構
/// </summary>
public class SchemaColumn
{
    /// <summary>
    /// 欄位名稱
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 資料型別（如 INT、NVARCHAR、DECIMAL）
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// 最大長度（適用於字串類型）
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// 精度（適用於數值類型）
    /// </summary>
    public int? Precision { get; set; }

    /// <summary>
    /// 小數位數（適用於數值類型）
    /// </summary>
    public int? Scale { get; set; }

    /// <summary>
    /// 是否允許 NULL
    /// </summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// 預設值
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// 是否為自增欄位
    /// </summary>
    public bool IsIdentity { get; set; }

    /// <summary>
    /// 定序
    /// </summary>
    public string? Collation { get; set; }

    // SQL Server 固定精度型別：不接受任何長度或精度參數
    private static readonly HashSet<string> FixedPrecisionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "datetime", "smalldatetime", "date",
        "timestamp", "rowversion",
        "int", "bigint", "smallint", "tinyint", "bit",
        "money", "smallmoney",
        "float", "real",
        "text", "ntext", "image",
        "xml", "uniqueidentifier", "sql_variant",
        "geography", "geometry", "hierarchyid"
    };

    /// <summary>
    /// 取得完整的資料型別字串
    /// </summary>
    public string GetFullDataType()
    {
        // 固定精度型別不加括號
        if (FixedPrecisionTypes.Contains(DataType))
            return DataType;

        // 有長度的類型（如 NVARCHAR、VARCHAR、CHAR）
        if (MaxLength.HasValue)
        {
            return $"{DataType}({MaxLength.Value})";
        }

        // 有精度和小數位的類型（如 DECIMAL、DATETIME2、TIME）
        if (Precision.HasValue && Scale.HasValue)
        {
            return $"{DataType}({Precision.Value},{Scale.Value})";
        }

        // 只有精度的類型
        if (Precision.HasValue)
        {
            return $"{DataType}({Precision.Value})";
        }

        return DataType;
    }
}
