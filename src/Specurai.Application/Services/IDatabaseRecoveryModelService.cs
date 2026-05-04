using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// 資料庫 Recovery Model 管理服務介面
/// </summary>
public interface IDatabaseRecoveryModelService
{
    Task<IEnumerable<DatabaseRecoveryModel>> GetAllAsync(CancellationToken ct = default);
    Task SaveChangesAsync(IEnumerable<(string DatabaseName, string NewRecoveryModel)> changes, CancellationToken ct = default);
}
