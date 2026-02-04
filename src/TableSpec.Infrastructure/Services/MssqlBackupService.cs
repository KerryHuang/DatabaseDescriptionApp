using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Enums;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Services;

/// <summary>
/// MSSQL 備份服務實作
/// </summary>
public class MssqlBackupService : IBackupService
{
    private readonly string _historyFilePath;
    private BackupHistory? _cachedHistory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MssqlBackupService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TableSpec");
        Directory.CreateDirectory(appDataPath);
        _historyFilePath = Path.Combine(appDataPath, "backup-history.json");
    }

    /// <inheritdoc />
    public async Task<BackupInfo> BackupDatabaseAsync(
        string connectionString,
        Guid connectionId,
        string connectionName,
        string backupPath,
        BackupType backupType = BackupType.Full,
        string? description = null,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var databaseName = connection.Database;
        var serverName = connection.DataSource;

        // 建立備份 SQL
        var backupName = $"{databaseName}-{backupType}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var backupSql = BuildBackupSql(databaseName, backupType, backupName);

        // 註冊進度事件
        connection.InfoMessage += (_, e) =>
        {
            var match = Regex.Match(e.Message, @"(\d+)\s*percent");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
            {
                progress?.Report(new BackupProgress
                {
                    PercentComplete = percent,
                    Message = $"備份進度: {percent}%"
                });
            }
        };

        progress?.Report(new BackupProgress
        {
            PercentComplete = 0,
            Message = "開始備份..."
        });

        // 執行備份
        await using var command = new SqlCommand(backupSql, connection);
        command.CommandTimeout = 0; // 無限制
        command.Parameters.AddWithValue("@BackupPath", backupPath);
        command.Parameters.AddWithValue("@BackupName", backupName);
        if (!string.IsNullOrEmpty(description))
        {
            command.Parameters.AddWithValue("@Description", description);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);

        progress?.Report(new BackupProgress
        {
            PercentComplete = 100,
            Message = "備份完成"
        });

        // 從 SQL Server 取得備份檔案大小
        var fileSize = await GetBackupFileSizeAsync(connection, databaseName, cancellationToken);

        // 取得 SQL Server 版本
        var sqlVersion = await GetSqlServerVersionAsync(connection, cancellationToken);

        var backupInfo = new BackupInfo
        {
            ConnectionId = connectionId,
            ConnectionName = connectionName,
            DatabaseName = databaseName,
            ServerName = serverName,
            BackupFilePath = backupPath,
            BackupTime = DateTime.Now,
            BackupType = backupType,
            FileSizeBytes = fileSize,
            IsVerified = false,
            Description = description,
            SqlServerVersion = sqlVersion
        };

        // 自動加入歷史記錄
        AddToHistory(backupInfo);

        return backupInfo;
    }

    /// <inheritdoc />
    public async Task RestoreDatabaseAsync(
        string connectionString,
        string backupPath,
        RestoreOptions options,
        IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 連接到 master 資料庫
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // 取得備份檔案資訊
        var fileInfo = await GetBackupFileInfoAsync(connectionString, backupPath, cancellationToken);
        var sourceDatabaseName = fileInfo.DatabaseName;
        var targetDatabaseName = options.Mode == RestoreMode.CreateNew
            ? options.TargetDatabaseName ?? $"{sourceDatabaseName}_Restored"
            : sourceDatabaseName;

        progress?.Report(new RestoreProgress
        {
            PercentComplete = 0,
            Message = $"準備還原到 {targetDatabaseName}..."
        });

        // 如果是覆蓋模式，先設定資料庫為單一使用者模式
        if (options.Mode == RestoreMode.OverwriteExisting)
        {
            await SetDatabaseSingleUserModeAsync(connection, targetDatabaseName, cancellationToken);
        }

        try
        {
            // 查詢 SQL Server 預設資料檔目錄（用於 MOVE 子句）
            var serverDefaultDataPath = await GetServerDefaultDataPathAsync(connection, cancellationToken);

            // 建立還原 SQL
            var restoreSql = BuildRestoreSql(targetDatabaseName, fileInfo, options, serverDefaultDataPath);

            // 註冊進度事件
            connection.InfoMessage += (_, e) =>
            {
                var match = Regex.Match(e.Message, @"(\d+)\s*percent");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                {
                    progress?.Report(new RestoreProgress
                    {
                        PercentComplete = percent,
                        Message = $"還原進度: {percent}%"
                    });
                }
            };

            // 執行還原
            await using var restoreCommand = new SqlCommand(restoreSql, connection);
            restoreCommand.CommandTimeout = 0;
            restoreCommand.Parameters.AddWithValue("@BackupPath", backupPath);
            await restoreCommand.ExecuteNonQueryAsync(cancellationToken);

            progress?.Report(new RestoreProgress
            {
                PercentComplete = 100,
                Message = "還原完成"
            });
        }
        finally
        {
            // 還原後設定為多使用者模式
            if (options.Mode == RestoreMode.OverwriteExisting && options.WithRecovery)
            {
                await SetDatabaseMultiUserModeAsync(connection, targetDatabaseName, cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task<BackupVerifyResult> VerifyBackupAsync(
        string connectionString,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(
                "RESTORE VERIFYONLY FROM DISK = @BackupPath", connection);
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@BackupPath", backupPath);

            await command.ExecuteNonQueryAsync(cancellationToken);

            var fileInfo = await GetBackupFileInfoAsync(connectionString, backupPath, cancellationToken);

            return new BackupVerifyResult
            {
                IsValid = true,
                FileInfo = fileInfo
            };
        }
        catch (Exception ex)
        {
            return new BackupVerifyResult
            {
                IsValid = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<BackupFileInfo> GetBackupFileInfoAsync(
        string connectionString,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // 取得備份標頭資訊
        var headerTable = new DataTable();
        await using (var headerCommand = new SqlCommand(
            "RESTORE HEADERONLY FROM DISK = @BackupPath", connection))
        {
            headerCommand.Parameters.AddWithValue("@BackupPath", backupPath);
            using var adapter = new SqlDataAdapter(headerCommand);
            adapter.Fill(headerTable);
        }

        var headerRow = headerTable.Rows[0];

        // 取得檔案清單
        var fileListTable = new DataTable();
        await using (var fileListCommand = new SqlCommand(
            "RESTORE FILELISTONLY FROM DISK = @BackupPath", connection))
        {
            fileListCommand.Parameters.AddWithValue("@BackupPath", backupPath);
            using var adapter = new SqlDataAdapter(fileListCommand);
            adapter.Fill(fileListTable);
        }

        var logicalFiles = fileListTable.AsEnumerable()
            .Select(row => new BackupLogicalFile
            {
                LogicalName = row.Field<string>("LogicalName") ?? "",
                PhysicalName = row.Field<string>("PhysicalName") ?? "",
                Type = row.Field<string>("Type") ?? "",
                SizeBytes = row.Field<long>("Size")
            })
            .ToList();

        var backupTypeValue = headerRow.Field<byte>("BackupType");
        var backupType = backupTypeValue switch
        {
            1 => BackupType.Full,
            5 => BackupType.Differential,
            2 => BackupType.TransactionLog,
            _ => BackupType.Full
        };

        return new BackupFileInfo
        {
            DatabaseName = headerRow.Field<string>("DatabaseName") ?? "",
            ServerName = headerRow.Field<string>("ServerName") ?? "",
            BackupStartTime = headerRow.Field<DateTime>("BackupStartDate"),
            BackupFinishTime = headerRow.Field<DateTime>("BackupFinishDate"),
            BackupType = backupType,
            BackupSizeBytes = headerRow.Field<long>("BackupSize"),
            SqlServerVersion = headerRow.Field<int>("SoftwareVersionMajor").ToString(),
            DatabaseVersion = headerRow.Field<int>("DatabaseVersion"),
            Description = headerRow.Field<string>("BackupDescription"),
            LogicalFiles = logicalFiles
        };
    }

    /// <inheritdoc />
    public BackupHistory GetBackupHistory()
    {
        if (_cachedHistory != null)
            return _cachedHistory;

        if (!File.Exists(_historyFilePath))
        {
            _cachedHistory = new BackupHistory();
            return _cachedHistory;
        }

        try
        {
            var json = File.ReadAllText(_historyFilePath);
            _cachedHistory = JsonSerializer.Deserialize<BackupHistory>(json, JsonOptions)
                ?? new BackupHistory();
        }
        catch
        {
            _cachedHistory = new BackupHistory();
        }

        return _cachedHistory;
    }

    /// <inheritdoc />
    public void SaveBackupHistory(BackupHistory history)
    {
        _cachedHistory = history;
        var json = JsonSerializer.Serialize(history, JsonOptions);
        File.WriteAllText(_historyFilePath, json);
    }

    /// <inheritdoc />
    public void AddToHistory(BackupInfo backupInfo)
    {
        var history = GetBackupHistory();
        history.Add(backupInfo);
        SaveBackupHistory(history);
    }

    /// <inheritdoc />
    public void RemoveFromHistory(Guid backupId)
    {
        var history = GetBackupHistory();
        history.Remove(backupId);
        SaveBackupHistory(history);
    }

    #region Private Methods

    private static string BuildBackupSql(string databaseName, BackupType backupType, string backupName)
    {
        return backupType switch
        {
            BackupType.Full => $@"
                BACKUP DATABASE [{databaseName}]
                TO DISK = @BackupPath
                WITH FORMAT, INIT, NAME = @BackupName,
                STATS = 10",

            BackupType.Differential => $@"
                BACKUP DATABASE [{databaseName}]
                TO DISK = @BackupPath
                WITH DIFFERENTIAL, NAME = @BackupName,
                STATS = 10",

            BackupType.TransactionLog => $@"
                BACKUP LOG [{databaseName}]
                TO DISK = @BackupPath
                WITH NAME = @BackupName,
                STATS = 10",

            _ => throw new ArgumentOutOfRangeException(nameof(backupType))
        };
    }

    private string BuildRestoreSql(
        string targetDatabaseName,
        BackupFileInfo fileInfo,
        RestoreOptions options,
        string? serverDefaultDataPath)
    {
        var sql = new StringBuilder();
        sql.AppendLine($"RESTORE DATABASE [{targetDatabaseName}]");
        sql.AppendLine("FROM DISK = @BackupPath");
        sql.AppendLine("WITH");

        var withClauses = new List<string>();

        if (options.WithReplace || options.Mode == RestoreMode.OverwriteExisting)
        {
            withClauses.Add("    REPLACE");
        }

        // 處理檔案重新配置（新資料庫時）
        if (options.Mode == RestoreMode.CreateNew)
        {
            foreach (var file in fileInfo.LogicalFiles)
            {
                var newPhysicalPath = file.Type == "D"
                    ? options.DataFilePath ?? GetDefaultFilePath(serverDefaultDataPath, targetDatabaseName, file.LogicalName, ".mdf")
                    : options.LogFilePath ?? GetDefaultFilePath(serverDefaultDataPath, targetDatabaseName, file.LogicalName, ".ldf");

                withClauses.Add($"    MOVE '{file.LogicalName}' TO '{newPhysicalPath}'");
            }
        }

        withClauses.Add(options.WithRecovery ? "    RECOVERY" : "    NORECOVERY");
        withClauses.Add("    STATS = 10");

        sql.AppendLine(string.Join(",\n", withClauses));

        return sql.ToString();
    }

    private static string GetDefaultFilePath(string? serverDefaultDataPath, string databaseName, string logicalName, string extension)
    {
        // 使用從 SQL Server 查詢到的預設資料目錄
        var basePath = !string.IsNullOrEmpty(serverDefaultDataPath)
            ? serverDefaultDataPath
            : @"C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA";

        return Path.Combine(basePath, $"{databaseName}_{logicalName}{extension}");
    }

    private static async Task SetDatabaseSingleUserModeAsync(
        SqlConnection connection,
        string databaseName,
        CancellationToken cancellationToken)
    {
        try
        {
            var sql = $@"
                IF EXISTS (SELECT 1 FROM sys.databases WHERE name = @DbName)
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                END";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@DbName", databaseName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // 資料庫可能不存在，忽略錯誤
        }
    }

    private static async Task SetDatabaseMultiUserModeAsync(
        SqlConnection connection,
        string databaseName,
        CancellationToken cancellationToken)
    {
        try
        {
            var sql = $"ALTER DATABASE [{databaseName}] SET MULTI_USER;";
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // 忽略錯誤
        }
    }

    private static async Task<string> GetSqlServerVersionAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT @@VERSION", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString()?.Split('\n').FirstOrDefault() ?? "";
    }

    /// <summary>
    /// 從 msdb 取得最近一次備份的檔案大小（伺服器端）
    /// </summary>
    private static async Task<long> GetBackupFileSizeAsync(
        SqlConnection connection,
        string databaseName,
        CancellationToken cancellationToken)
    {
        try
        {
            const string sql = @"
                SELECT TOP 1 ISNULL(compressed_backup_size, backup_size)
                FROM msdb.dbo.backupset
                WHERE database_name = @DatabaseName
                ORDER BY backup_finish_date DESC";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@DatabaseName", databaseName);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is decimal size ? (long)size : 0;
        }
        catch
        {
            // msdb 查詢失敗時回傳 0
            return 0;
        }
    }

    /// <summary>
    /// 查詢 SQL Server 伺服器端的預設資料檔目錄
    /// </summary>
    private static async Task<string?> GetServerDefaultDataPathAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            const string sql = "SELECT SERVERPROPERTY('InstanceDefaultDataPath')";
            await using var command = new SqlCommand(sql, connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result?.ToString();
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
