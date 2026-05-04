using Specurai.Domain.Entities;

namespace Specurai.Domain.Interfaces;

/// <summary>
/// 資料庫 Recovery Model 資料存取介面
/// </summary>
public interface IDatabaseRecoveryModelRepository
{
    Task<IEnumerable<DatabaseRecoveryModel>> GetAllAsync(CancellationToken ct = default);
    Task SetRecoveryModelAsync(string databaseName, string recoveryModel, CancellationToken ct = default);
}
