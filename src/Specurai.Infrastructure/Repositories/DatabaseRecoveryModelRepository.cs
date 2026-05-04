using Dapper;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Infrastructure.Repositories;

/// <summary>
/// 資料庫 Recovery Model 資料存取 Repository
/// </summary>
public class DatabaseRecoveryModelRepository : IDatabaseRecoveryModelRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public DatabaseRecoveryModelRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IEnumerable<DatabaseRecoveryModel>> GetAllAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return [];

        const string sql = @"
SELECT
    name AS DatabaseName,
    recovery_model_desc AS RecoveryModel
FROM sys.databases
ORDER BY name;";

        await using var conn = new SqlConnection(connectionString);
        return await conn.QueryAsync<DatabaseRecoveryModel>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task SetRecoveryModelAsync(string databaseName, string recoveryModel, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        // databaseName 僅來自 sys.databases 查詢結果，不接受使用者直接輸入
        var sql = recoveryModel == "SIMPLE"
            ? $"ALTER DATABASE [{databaseName}] SET RECOVERY SIMPLE;"
            : $"ALTER DATABASE [{databaseName}] SET RECOVERY FULL;";

        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }
}
