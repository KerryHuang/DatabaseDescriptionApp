namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 索引結構
/// </summary>
public class SchemaIndex
{
    /// <summary>
    /// 索引名稱
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否為叢集索引
    /// </summary>
    public bool IsClustered { get; set; }

    /// <summary>
    /// 是否為唯一索引
    /// </summary>
    public bool IsUnique { get; set; }

    /// <summary>
    /// 索引欄位清單
    /// </summary>
    public IList<string> Columns { get; set; } = new List<string>();

    /// <summary>
    /// 涵蓋欄位清單
    /// </summary>
    public IList<string> IncludeColumns { get; set; } = new List<string>();

    /// <summary>
    /// 篩選條件
    /// </summary>
    public string? FilterDefinition { get; set; }

    /// <summary>
    /// 取得索引類型字串
    /// </summary>
    public string GetIndexType()
    {
        var type = IsClustered ? "CLUSTERED" : "NONCLUSTERED";
        if (IsUnique)
        {
            type += " UNIQUE";
        }
        return type;
    }

    /// <summary>
    /// 取得欄位組合簽章
    /// </summary>
    public string GetColumnsSignature()
    {
        return string.Join(", ", Columns);
    }
}
