namespace Specurai.Domain.Entities;

/// <summary>
/// 資料庫 Recovery Model 資訊
/// </summary>
public class DatabaseRecoveryModel
{
    public required string DatabaseName { get; init; }
    public required string RecoveryModel { get; init; }
}
