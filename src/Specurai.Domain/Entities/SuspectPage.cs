namespace Specurai.Domain.Entities;

/// <summary>msdb.dbo.suspect_pages 單筆紀錄</summary>
public class SuspectPage
{
    public required string DatabaseName { get; init; }
    public required int FileId { get; init; }
    public required long PageId { get; init; }
    /// <summary>原始 event_type 數值</summary>
    public required int EventTypeRaw { get; init; }
    public required int ErrorCount { get; init; }
    public required DateTime LastUpdateDate { get; init; }

    /// <summary>event_type 中文解碼</summary>
    public string EventTypeText => EventTypeRaw switch
    {
        1 => "824 錯誤",
        2 => "不正常 shutdown",
        3 => "校驗失敗",
        4 => "已從備份還原",
        5 => "已修復",
        7 => "已 deallocate",
        _ => $"未知 ({EventTypeRaw})"
    };
}
