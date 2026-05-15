using Dapper;
using Microsoft.Data.SqlClient;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Infrastructure.Repositories;

/// <summary>
/// 資料庫資訊查詢 Repository 實作（用於維護計劃前置檢查）
/// </summary>
public class DatabaseInfoRepository : IDatabaseInfoRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public DatabaseInfoRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    /// <summary>
    /// SQL 名稱引號保護，防止 SQL Injection
    /// </summary>
    private static string QuoteName(string name) => $"[{name.Replace("]", "]]")}]";

    public async Task<IReadOnlyList<string>> GetDatabaseNamesAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<string>();

        const string sql = "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<string> GetRecoveryModelAsync(string databaseName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return "UNKNOWN";

        const string sql = "SELECT recovery_model_desc FROM sys.databases WHERE name = @DatabaseName";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { DatabaseName = databaseName }, cancellationToken: ct));
        return result ?? "UNKNOWN";
    }

    public async Task<IReadOnlyList<(string LogicalName, string PhysicalName)>> GetLogicalFileNamesAsync(string databaseName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<(string, string)>();

        const string sql = "SELECT name, physical_name FROM sys.master_files WHERE database_id = DB_ID(@DatabaseName)";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<(string, string)>(new CommandDefinition(sql, new { DatabaseName = databaseName }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<bool> LoginExistsAsync(string loginName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return false;

        const string sql = "SELECT COUNT(1) FROM sys.server_principals WHERE name = @LoginName";

        await using var connection = new SqlConnection(connectionString);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { LoginName = loginName }, cancellationToken: ct));
        return count > 0;
    }

    public async Task<bool> DatabaseUserExistsAsync(string databaseName, string userName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return false;

        var safeName = QuoteName(databaseName);
        var sql = $"SELECT COUNT(1) FROM {safeName}.sys.database_principals WHERE name = @UserName";

        await using var connection = new SqlConnection(connectionString);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { UserName = userName }, cancellationToken: ct));
        return count > 0;
    }

    public async Task<bool> IsDbOwnerMemberAsync(string databaseName, string userName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return false;

        var safeName = QuoteName(databaseName);
        var sql = $@"
SELECT COUNT(1)
FROM {safeName}.sys.database_role_members rm
JOIN {safeName}.sys.database_principals r ON rm.role_principal_id = r.principal_id
JOIN {safeName}.sys.database_principals m ON rm.member_principal_id = m.principal_id
WHERE r.name = 'db_owner' AND m.name = @UserName";

        await using var connection = new SqlConnection(connectionString);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { UserName = userName }, cancellationToken: ct));
        return count > 0;
    }

    public async Task<bool> AgentJobExistsAsync(string jobName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return false;

        const string sql = "SELECT COUNT(1) FROM msdb.dbo.sysjobs WHERE name = @JobName";

        await using var connection = new SqlConnection(connectionString);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { JobName = jobName }, cancellationToken: ct));
        return count > 0;
    }

    public async Task<int> GetCompatibilityLevelAsync(string databaseName, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return 0;

        const string sql = "SELECT compatibility_level FROM sys.databases WHERE name = @DatabaseName";

        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { DatabaseName = databaseName }, cancellationToken: ct));
    }

    public async Task<int> GetServerCompatibilityLevelAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return 0;

        // SQL Server 版本主版號 × 10 = 相容性層級（例如 16.x → 160）
        const string sql = "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) * 10";

        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<bool> IsAzureSqlDatabaseAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return false;

        const string sql = "SELECT CAST(SERVERPROPERTY('EngineEdition') AS INT)";

        await using var connection = new SqlConnection(connectionString);
        var edition = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
        return edition == 5;
    }

    public async Task ExecuteSqlWithTransactionAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task ExecuteSqlAsync(string sql, CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return;

        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DatabaseFileInfo>> GetDatabaseFilesAsync(string databaseName, CancellationToken ct = default)
    {
        var connStr = _connectionStringProvider() ?? throw new InvalidOperationException("未設定連線字串");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        // 切換到目標 DB 後查 sys.database_files + sys.dm_os_volume_stats
        // 注意：FILEPROPERTY 必須在目標 DB 的 context 才能解析
        var sql = $@"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
USE [{databaseName.Replace("]", "]]")}];
SELECT
    f.name                                              AS LogicalName,
    f.physical_name                                     AS PhysicalName,
    f.type                                              AS FileTypeRaw,
    CAST(f.size * 8 / 1024 AS INT)                      AS SizeMB,
    CAST((f.size - FILEPROPERTY(f.name, 'SpaceUsed')) * 8 / 1024 AS INT) AS FreeMB,
    f.is_percent_growth                                 AS IsPercentGrowth,
    CASE WHEN f.is_percent_growth = 1
         THEN f.growth
         ELSE CAST(f.growth * 8 / 1024 AS INT) END     AS GrowthMB,
    vs.volume_mount_point                               AS VolumeMountPoint,
    CAST(vs.available_bytes / 1073741824 AS INT)        AS VolumeFreeGB
FROM sys.database_files f
OUTER APPLY sys.dm_os_volume_stats(DB_ID(), f.file_id) vs;
";

        var rows = await conn.QueryAsync<DatabaseFileRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r => new DatabaseFileInfo
        {
            LogicalName = r.LogicalName,
            PhysicalName = r.PhysicalName,
            FileType = r.FileTypeRaw == 1 ? DatabaseFileType.Log : DatabaseFileType.Data,
            SizeMB = r.SizeMB,
            FreeMB = r.FreeMB,
            IsPercentGrowth = r.IsPercentGrowth,
            GrowthMB = r.GrowthMB,
            VolumeMountPoint = r.VolumeMountPoint ?? string.Empty,
            VolumeFreeGB = r.VolumeFreeGB
        }).ToList();
    }

    private sealed class DatabaseFileRow
    {
        public string LogicalName { get; set; } = string.Empty;
        public string PhysicalName { get; set; } = string.Empty;
        public byte FileTypeRaw { get; set; }
        public int SizeMB { get; set; }
        public int FreeMB { get; set; }
        public bool IsPercentGrowth { get; set; }
        public int GrowthMB { get; set; }
        public string? VolumeMountPoint { get; set; }
        public int? VolumeFreeGB { get; set; }
    }
}
