namespace Specurai.Domain.Entities;

/// <summary>資料庫檔案類型</summary>
public enum DatabaseFileType
{
    Data = 0,
    Log = 1
}

/// <summary>資料庫檔案資訊（含所在磁碟空間）</summary>
public class DatabaseFileInfo
{
    public required string LogicalName { get; init; }
    public required string PhysicalName { get; init; }
    public required DatabaseFileType FileType { get; init; }
    /// <summary>檔案目前大小（MB）</summary>
    public required int SizeMB { get; init; }
    /// <summary>檔案內可用空間（MB）= Size - SpaceUsed</summary>
    public required int FreeMB { get; init; }
    /// <summary>autogrowth 是否為百分比模式</summary>
    public required bool IsPercentGrowth { get; init; }
    /// <summary>autogrowth 數值；IsPercentGrowth=false 時單位為 MB，true 時為百分比</summary>
    public required int GrowthMB { get; init; }
    /// <summary>檔案所在磁碟掛載點（Windows 為 "D:\\"，Linux 為 "/"）</summary>
    public required string VolumeMountPoint { get; init; }
    /// <summary>檔案所在磁碟可用空間（GB）；查不到時為 null</summary>
    public int? VolumeFreeGB { get; init; }

    /// <summary>檔案內可用空間百分比</summary>
    public decimal FreePercent => SizeMB == 0 ? 0m : (decimal)FreeMB * 100m / SizeMB;
}
