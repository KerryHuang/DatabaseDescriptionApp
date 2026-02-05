namespace TableSpec.Domain.Entities;

/// <summary>
/// 資料表使用狀態資訊
/// </summary>
public class TableUsageInfo
{
    /// <summary>Schema 名稱</summary>
    public required string SchemaName { get; init; }

    /// <summary>資料表名稱</summary>
    public required string TableName { get; init; }

    /// <summary>索引搜尋次數</summary>
    public long UserSeeks { get; init; }

    /// <summary>索引掃描次數</summary>
    public long UserScans { get; init; }

    /// <summary>索引查找次數</summary>
    public long UserLookups { get; init; }

    /// <summary>更新次數</summary>
    public long UserUpdates { get; init; }

    /// <summary>最後索引搜尋時間</summary>
    public DateTime? LastUserSeek { get; init; }

    /// <summary>最後索引掃描時間</summary>
    public DateTime? LastUserScan { get; init; }

    /// <summary>最後更新時間</summary>
    public DateTime? LastUserUpdate { get; init; }

    /// <summary>是否有查詢活動（seek/lookup > 0，排除 scan 因工具掃描會產生干擾）</summary>
    public bool HasQueryActivity => UserSeeks + UserLookups > 0;

    /// <summary>近 N 年是否有更新</summary>
    public bool HasRecentUpdate(int years)
        => LastUserUpdate.HasValue && LastUserUpdate.Value > DateTime.Now.AddYears(-years);

    /// <summary>完整表名</summary>
    public string FullTableName => $"[{SchemaName}].[{TableName}]";

    /// <summary>DROP TABLE 語法</summary>
    public string DropTableStatement => $"DROP TABLE [{SchemaName}].[{TableName}]";
}
