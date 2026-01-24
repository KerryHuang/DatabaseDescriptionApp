namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 表格結構
/// </summary>
public class SchemaTable
{
    /// <summary>
    /// Schema 名稱
    /// </summary>
    public string Schema { get; set; } = "dbo";

    /// <summary>
    /// 表格名稱
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 欄位清單
    /// </summary>
    public IList<SchemaColumn> Columns { get; set; } = new List<SchemaColumn>();

    /// <summary>
    /// 索引清單
    /// </summary>
    public IList<SchemaIndex> Indexes { get; set; } = new List<SchemaIndex>();

    /// <summary>
    /// 約束清單
    /// </summary>
    public IList<SchemaConstraint> Constraints { get; set; } = new List<SchemaConstraint>();

    /// <summary>
    /// 取得完整表格名稱
    /// </summary>
    public string FullName => $"[{Schema}].[{Name}]";

    /// <summary>
    /// 根據名稱取得欄位
    /// </summary>
    public SchemaColumn? GetColumn(string name)
    {
        return Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 取得主鍵約束
    /// </summary>
    public SchemaConstraint? GetPrimaryKey()
    {
        return Constraints.FirstOrDefault(c => c.ConstraintType == ConstraintType.PrimaryKey);
    }

    /// <summary>
    /// 取得所有外鍵約束
    /// </summary>
    public IEnumerable<SchemaConstraint> GetForeignKeys()
    {
        return Constraints.Where(c => c.ConstraintType == ConstraintType.ForeignKey);
    }
}
