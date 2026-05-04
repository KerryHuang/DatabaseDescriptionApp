using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// 資料庫 Recovery Model 管理服務
/// </summary>
public class DatabaseRecoveryModelService : IDatabaseRecoveryModelService
{
    private readonly IDatabaseRecoveryModelRepository _repository;

    public DatabaseRecoveryModelService(IDatabaseRecoveryModelRepository repository)
    {
        _repository = repository;
    }

    public Task<IEnumerable<DatabaseRecoveryModel>> GetAllAsync(CancellationToken ct = default)
        => _repository.GetAllAsync(ct);

    public async Task SaveChangesAsync(IEnumerable<(string DatabaseName, string NewRecoveryModel)> changes, CancellationToken ct = default)
    {
        foreach (var (databaseName, newRecoveryModel) in changes)
            await _repository.SetRecoveryModelAsync(databaseName, newRecoveryModel, ct);
    }
}
