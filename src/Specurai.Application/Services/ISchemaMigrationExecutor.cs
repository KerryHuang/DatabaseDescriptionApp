using Specurai.Domain.Entities.SchemaCompare;

namespace Specurai.Application.Services;

/// <summary>
/// Schema Migration 執行器介面
/// </summary>
public interface ISchemaMigrationExecutor
{
    /// <summary>
    /// 執行 Migration 腳本並回傳執行報告
    /// </summary>
    /// <param name="script">要執行的同步腳本</param>
    /// <param name="targetConnectionString">目標資料庫連線字串</param>
    /// <param name="ct">取消權杖</param>
    Task<MigrationReport> ExecuteAsync(
        SyncScript script,
        string targetConnectionString,
        bool dryRun = false,
        CancellationToken ct = default);

    /// <summary>
    /// 將目標資料庫的 transaction log 檔調整到指定大小（MB）：
    ///   - 目標 &gt; 現大小：ALTER DATABASE MODIFY FILE 預擴（避免下次 migration 時 autogrow 卡寫入）
    ///   - 目標 &lt; 現大小：CHECKPOINT + DBCC SHRINKFILE 縮小（回收 LDF 物理空間）
    ///   - 相等：不做任何事
    /// </summary>
    /// <param name="targetConnectionString">目標資料庫連線字串</param>
    /// <param name="targetSizeMb">目標 LDF 大小（MB），建議 1024 ~ 20480 之間</param>
    /// <param name="ct">取消權杖</param>
    Task<ResizeLogResult> ResizeLogAsync(
        string targetConnectionString,
        int targetSizeMb,
        CancellationToken ct = default);
}

/// <summary>調整 transaction log 大小的結果</summary>
public sealed class ResizeLogResult
{
    public bool IsSuccess { get; init; }
    public double BeforeSizeMb { get; init; }
    public double AfterSizeMb { get; init; }
    /// <summary>操作類型：Shrink / Grow / NoChange</summary>
    public string Operation { get; init; } = "NoChange";
    public string? ErrorMessage { get; init; }
    public string? LogReuseWait { get; init; }
}
