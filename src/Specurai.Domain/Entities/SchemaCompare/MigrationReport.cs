using Specurai.Domain.Enums;

namespace Specurai.Domain.Entities.SchemaCompare;

/// <summary>
/// Migration 完整執行報告
/// </summary>
public class MigrationReport
{
    /// <summary>
    /// 基準環境名稱
    /// </summary>
    public string BaseEnvironment { get; set; } = string.Empty;

    /// <summary>
    /// 目標環境名稱
    /// </summary>
    public string TargetEnvironment { get; set; } = string.Empty;

    /// <summary>
    /// 執行時間
    /// </summary>
    public DateTime ExecutedAt { get; set; }

    /// <summary>
    /// 總耗時
    /// </summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// 是否整體成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 整體錯誤訊息（失敗時）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 執行日誌清單
    /// </summary>
    public IList<MigrationLogEntry> Entries { get; set; } = new List<MigrationLogEntry>();

    /// <summary>
    /// 實際執行的 SQL 腳本
    /// </summary>
    public string AppliedScript { get; set; } = string.Empty;

    /// <summary>
    /// 是否為 Dry Run（模擬執行，不實際提交）
    /// </summary>
    public bool IsDryRun { get; set; }

    /// <summary>
    /// 失敗時：實際造成錯誤的 SQL 語句（從腳本行號提取）
    /// </summary>
    public string? FailedStatement { get; set; }

    /// <summary>
    /// 成功執行的筆數
    /// </summary>
    public int SuccessCount => Entries.Count(e => e.Status == MigrationLogStatus.Success);

    /// <summary>
    /// 略過的筆數（使用者未勾選 + 高風險）
    /// </summary>
    public int SkippedCount => Entries.Count(e =>
        e.Status == MigrationLogStatus.Skipped ||
        e.Status == MigrationLogStatus.HighRisk);
}
