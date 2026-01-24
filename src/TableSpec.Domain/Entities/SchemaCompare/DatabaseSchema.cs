namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 資料庫 Schema 快照
/// </summary>
public class DatabaseSchema
{
    /// <summary>
    /// 連線名稱
    /// </summary>
    public string ConnectionName { get; set; } = string.Empty;

    /// <summary>
    /// 資料庫名稱
    /// </summary>
    public string? DatabaseName { get; set; }

    /// <summary>
    /// 伺服器名稱
    /// </summary>
    public string? ServerName { get; set; }

    /// <summary>
    /// 收集時間
    /// </summary>
    public DateTime CollectedAt { get; set; }

    /// <summary>
    /// 表格清單
    /// </summary>
    public IList<SchemaTable> Tables { get; set; } = new List<SchemaTable>();

    /// <summary>
    /// 檢視表清單
    /// </summary>
    public IList<SchemaProgramObject> Views { get; set; } = new List<SchemaProgramObject>();

    /// <summary>
    /// 預存程序清單
    /// </summary>
    public IList<SchemaProgramObject> StoredProcedures { get; set; } = new List<SchemaProgramObject>();

    /// <summary>
    /// 函數清單
    /// </summary>
    public IList<SchemaProgramObject> Functions { get; set; } = new List<SchemaProgramObject>();

    /// <summary>
    /// 觸發程序清單
    /// </summary>
    public IList<SchemaProgramObject> Triggers { get; set; } = new List<SchemaProgramObject>();

    /// <summary>
    /// 總物件數量
    /// </summary>
    public int TotalObjectCount =>
        Tables.Count + Views.Count + StoredProcedures.Count + Functions.Count + Triggers.Count;

    /// <summary>
    /// 根據 Schema 和名稱取得表格
    /// </summary>
    public SchemaTable? GetTable(string schema, string name)
    {
        return Tables.FirstOrDefault(t =>
            t.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase) &&
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 取得所有表格的完整名稱
    /// </summary>
    public IEnumerable<string> GetAllTableNames()
    {
        return Tables.Select(t => t.FullName);
    }
}
