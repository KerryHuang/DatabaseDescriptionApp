using System.Text.RegularExpressions;

namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 程式物件類型
/// </summary>
public enum ProgramObjectType
{
    /// <summary>
    /// 檢視表
    /// </summary>
    View = 0,

    /// <summary>
    /// 預存程序
    /// </summary>
    StoredProcedure = 1,

    /// <summary>
    /// 函數
    /// </summary>
    Function = 2,

    /// <summary>
    /// 觸發程序
    /// </summary>
    Trigger = 3
}

/// <summary>
/// 程式物件結構（View、SP、Function、Trigger）
/// </summary>
public partial class SchemaProgramObject
{
    /// <summary>
    /// Schema 名稱
    /// </summary>
    public string Schema { get; set; } = "dbo";

    /// <summary>
    /// 物件名稱
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 物件類型
    /// </summary>
    public ProgramObjectType ObjectType { get; set; }

    /// <summary>
    /// 定義內容（SQL 語法）
    /// </summary>
    public string? Definition { get; set; }

    /// <summary>
    /// 取得完整物件名稱
    /// </summary>
    public string FullName => $"[{Schema}].[{Name}]";

    /// <summary>
    /// 取得正規化後的定義（移除格式差異）
    /// </summary>
    public string GetNormalizedDefinition()
    {
        if (string.IsNullOrEmpty(Definition))
            return string.Empty;

        // 移除多餘空白，統一為單一空格
        var normalized = WhitespaceRegex().Replace(Definition, " ");
        return normalized.Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
