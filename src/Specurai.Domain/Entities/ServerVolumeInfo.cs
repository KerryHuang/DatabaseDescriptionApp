namespace Specurai.Domain.Entities;

/// <summary>
/// 伺服器磁碟區空間資訊
/// </summary>
public sealed class ServerVolumeInfo
{
    /// <summary>磁碟名稱或掛載點（例：C:\ 或 /var/opt/mssql）</summary>
    public required string Name { get; init; }

    /// <summary>磁碟區標籤（可空）</summary>
    public string? Label { get; init; }

    /// <summary>可用空間（bytes）</summary>
    public long FreeBytes { get; init; }

    /// <summary>總空間（bytes）；無法取得時為 null（例如無資料庫檔案的空碟）</summary>
    public long? TotalBytes { get; init; }

    /// <summary>使用率（百分比）；無總量時為 null</summary>
    public double? UsedPercent =>
        TotalBytes is > 0 ? (double)(TotalBytes.Value - FreeBytes) / TotalBytes.Value * 100 : null;

    /// <summary>供進度條綁定的使用率值（無總量時為 0）</summary>
    public double UsedPercentValue => UsedPercent ?? 0;

    /// <summary>可用空間是否偏低（可用 &lt; 總量的 10%）</summary>
    public bool IsLowSpace => TotalBytes is > 0 && FreeBytes < TotalBytes.Value * 0.10;

    /// <summary>格式化的可用空間</summary>
    public string FormattedFree => FormatBytes(FreeBytes);

    /// <summary>格式化的總空間（無法取得時顯示「—」）</summary>
    public string FormattedTotal => TotalBytes.HasValue ? FormatBytes(TotalBytes.Value) : "—";

    /// <summary>使用率文字（無總量時「—」；偏低時加註 ⚠）</summary>
    public string UsedPercentText =>
        UsedPercent is null ? "—" : $"{UsedPercent.Value:F0}%{(IsLowSpace ? " ⚠" : string.Empty)}";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F0} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
