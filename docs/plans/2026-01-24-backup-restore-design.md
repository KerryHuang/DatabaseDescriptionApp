# 資料庫備份與還原功能實作計畫

> 建立日期：2026-01-24
> 狀態：✅ 已完成
> 優先級：高（Schema Compare 功能的前置需求）

---

## 一、專案概述

### 1.1 目標

在現有 TableSpec 專案中新增「資料庫備份與還原」功能，作為獨立功能模組，同時為後續 Schema Compare 功能提供安全保障。

### 1.2 功能需求

| 功能 | 說明 |
|------|------|
| **備份資料庫** | 將選定的資料庫備份到用戶指定的路徑 |
| **還原資料庫** | 從備份檔還原，可選擇覆蓋原資料庫或建立新資料庫 |
| **備份歷史** | 記錄備份歷史，方便快速還原 |
| **備份驗證** | 驗證備份檔案是否完整可用 |

### 1.3 使用情境

1. **手動備份**：用戶主動備份重要資料庫
2. **Schema Compare 前置**：執行結構比較前強制備份
3. **災難復原**：從備份檔還原資料庫

---

## 二、架構設計

### 2.1 整體架構

```
┌─────────────────────────────────────────────────────────────────┐
│                        Desktop 層 (MDI 架構)                     │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  MainWindow.axaml (TabControl 容器)                      │   │
│  │  └── BackupRestoreDocumentView.axaml (UserControl)       │   │
│  │       └── BackupRestoreDocumentViewModel                 │   │
│  │            ├── 備份功能（選擇資料庫、指定路徑）            │   │
│  │            ├── 還原功能（選擇備份檔、目標資料庫）          │   │
│  │            └── 備份歷史管理                               │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Application 層                              │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  IBackupService                                          │   │
│  │  ├── BackupDatabaseAsync(connectionId, backupPath)      │   │
│  │  ├── RestoreDatabaseAsync(backupPath, targetDb, options)│   │
│  │  ├── VerifyBackupAsync(backupPath)                      │   │
│  │  └── GetBackupHistoryAsync(connectionId)                │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Infrastructure 層                            │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  MssqlBackupService                                      │   │
│  │  ├── 執行 BACKUP DATABASE 命令                           │   │
│  │  ├── 執行 RESTORE DATABASE 命令                          │   │
│  │  ├── 執行 RESTORE VERIFYONLY 命令                        │   │
│  │  └── 管理備份歷史記錄（JSON 持久化）                      │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        Domain 層                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Entities                                                │   │
│  │  ├── BackupInfo          # 備份資訊                      │   │
│  │  ├── BackupHistory       # 備份歷史記錄                  │   │
│  │  └── RestoreOptions      # 還原選項                      │   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Interfaces                                              │   │
│  │  └── IBackupService                                     │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 資料流程

```
備份流程:
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│ 選擇連線     │ → │ 指定備份路徑 │ → │ 執行備份     │ → │ 驗證備份     │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
                                                                │
                                                                ▼
                                                       ┌─────────────┐
                                                       │ 記錄歷史     │
                                                       └─────────────┘

還原流程:
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│ 選擇備份檔   │ → │ 驗證備份檔   │ → │ 選擇還原目標 │ → │ 確認並執行   │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
```

---

## 三、Domain 層設計

### 3.1 新增檔案清單

```
src/TableSpec.Domain/
├── Entities/
│   ├── BackupInfo.cs
│   ├── BackupHistory.cs
│   └── RestoreOptions.cs
├── Enums/
│   ├── BackupType.cs
│   └── RestoreMode.cs
└── Interfaces/
    └── IBackupService.cs
```

### 3.2 實體設計

#### BackupInfo.cs

```csharp
namespace TableSpec.Domain.Entities;

/// <summary>
/// 備份資訊
/// </summary>
public class BackupInfo
{
    /// <summary>備份 ID</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>連線設定檔 ID</summary>
    public Guid ConnectionId { get; init; }

    /// <summary>連線名稱</summary>
    public string ConnectionName { get; init; } = string.Empty;

    /// <summary>資料庫名稱</summary>
    public string DatabaseName { get; init; } = string.Empty;

    /// <summary>伺服器名稱</summary>
    public string ServerName { get; init; } = string.Empty;

    /// <summary>備份檔案路徑</summary>
    public string BackupFilePath { get; init; } = string.Empty;

    /// <summary>備份時間</summary>
    public DateTime BackupTime { get; init; }

    /// <summary>備份類型</summary>
    public BackupType BackupType { get; init; }

    /// <summary>備份檔案大小（bytes）</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>備份是否已驗證</summary>
    public bool IsVerified { get; init; }

    /// <summary>備份描述/備註</summary>
    public string? Description { get; init; }

    /// <summary>SQL Server 版本</summary>
    public string SqlServerVersion { get; init; } = string.Empty;

    /// <summary>
    /// 格式化的檔案大小
    /// </summary>
    public string FormattedFileSize
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F2} KB";
            if (FileSizeBytes < 1024 * 1024 * 1024) return $"{FileSizeBytes / (1024.0 * 1024):F2} MB";
            return $"{FileSizeBytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
```

#### BackupHistory.cs

```csharp
namespace TableSpec.Domain.Entities;

/// <summary>
/// 備份歷史記錄集合
/// </summary>
public class BackupHistory
{
    /// <summary>所有備份記錄</summary>
    public List<BackupInfo> Backups { get; set; } = [];

    /// <summary>
    /// 取得指定連線的備份記錄
    /// </summary>
    public IEnumerable<BackupInfo> GetByConnection(Guid connectionId) =>
        Backups.Where(b => b.ConnectionId == connectionId)
               .OrderByDescending(b => b.BackupTime);

    /// <summary>
    /// 取得指定連線的最新備份
    /// </summary>
    public BackupInfo? GetLatestBackup(Guid connectionId) =>
        GetByConnection(connectionId).FirstOrDefault();

    /// <summary>
    /// 檢查是否有 24 小時內的備份
    /// </summary>
    public bool HasRecentBackup(Guid connectionId, TimeSpan maxAge) =>
        GetByConnection(connectionId)
            .Any(b => DateTime.Now - b.BackupTime <= maxAge);
}
```

#### RestoreOptions.cs

```csharp
namespace TableSpec.Domain.Entities;

/// <summary>
/// 還原選項
/// </summary>
public class RestoreOptions
{
    /// <summary>還原模式</summary>
    public RestoreMode Mode { get; init; }

    /// <summary>目標資料庫名稱（新資料庫時使用）</summary>
    public string? TargetDatabaseName { get; init; }

    /// <summary>資料檔案路徑（新資料庫時可指定）</summary>
    public string? DataFilePath { get; init; }

    /// <summary>日誌檔案路徑（新資料庫時可指定）</summary>
    public string? LogFilePath { get; init; }

    /// <summary>是否覆蓋現有資料庫</summary>
    public bool WithReplace { get; init; }

    /// <summary>還原後是否立即可用</summary>
    public bool WithRecovery { get; init; } = true;

    /// <summary>是否顯示進度</summary>
    public bool ShowProgress { get; init; } = true;
}
```

#### 列舉定義

```csharp
// BackupType.cs
namespace TableSpec.Domain.Enums;

/// <summary>
/// 備份類型
/// </summary>
public enum BackupType
{
    /// <summary>完整備份</summary>
    Full,

    /// <summary>差異備份</summary>
    Differential,

    /// <summary>交易記錄備份</summary>
    TransactionLog
}

// RestoreMode.cs
namespace TableSpec.Domain.Enums;

/// <summary>
/// 還原模式
/// </summary>
public enum RestoreMode
{
    /// <summary>覆蓋原資料庫</summary>
    OverwriteExisting,

    /// <summary>還原到新資料庫</summary>
    CreateNew
}
```

### 3.3 服務介面

#### IBackupService.cs

```csharp
namespace TableSpec.Domain.Interfaces;

/// <summary>
/// 資料庫備份服務介面
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// 備份資料庫
    /// </summary>
    /// <param name="connectionString">連線字串</param>
    /// <param name="connectionId">連線設定檔 ID</param>
    /// <param name="connectionName">連線名稱</param>
    /// <param name="backupPath">備份檔案路徑</param>
    /// <param name="backupType">備份類型</param>
    /// <param name="description">備份描述</param>
    /// <param name="progress">進度回報</param>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>備份資訊</returns>
    Task<BackupInfo> BackupDatabaseAsync(
        string connectionString,
        Guid connectionId,
        string connectionName,
        string backupPath,
        BackupType backupType = BackupType.Full,
        string? description = null,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 還原資料庫
    /// </summary>
    /// <param name="connectionString">連線字串（連接到 master 資料庫）</param>
    /// <param name="backupPath">備份檔案路徑</param>
    /// <param name="options">還原選項</param>
    /// <param name="progress">進度回報</param>
    /// <param name="cancellationToken">取消權杖</param>
    Task RestoreDatabaseAsync(
        string connectionString,
        string backupPath,
        RestoreOptions options,
        IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 驗證備份檔案
    /// </summary>
    /// <param name="connectionString">連線字串</param>
    /// <param name="backupPath">備份檔案路徑</param>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>是否有效</returns>
    Task<BackupVerifyResult> VerifyBackupAsync(
        string connectionString,
        string backupPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得備份檔案資訊
    /// </summary>
    /// <param name="connectionString">連線字串</param>
    /// <param name="backupPath">備份檔案路徑</param>
    /// <param name="cancellationToken">取消權杖</param>
    Task<BackupFileInfo> GetBackupFileInfoAsync(
        string connectionString,
        string backupPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得備份歷史記錄
    /// </summary>
    BackupHistory GetBackupHistory();

    /// <summary>
    /// 儲存備份歷史記錄
    /// </summary>
    void SaveBackupHistory(BackupHistory history);

    /// <summary>
    /// 新增備份記錄到歷史
    /// </summary>
    void AddToHistory(BackupInfo backupInfo);

    /// <summary>
    /// 從歷史中移除備份記錄
    /// </summary>
    void RemoveFromHistory(Guid backupId);
}

/// <summary>
/// 備份進度
/// </summary>
public class BackupProgress
{
    public int PercentComplete { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 還原進度
/// </summary>
public class RestoreProgress
{
    public int PercentComplete { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 備份驗證結果
/// </summary>
public class BackupVerifyResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public BackupFileInfo? FileInfo { get; init; }
}

/// <summary>
/// 備份檔案資訊
/// </summary>
public class BackupFileInfo
{
    public string DatabaseName { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public DateTime BackupStartTime { get; init; }
    public DateTime BackupFinishTime { get; init; }
    public BackupType BackupType { get; init; }
    public long BackupSizeBytes { get; init; }
    public string SqlServerVersion { get; init; } = string.Empty;
    public int DatabaseVersion { get; init; }
    public string? Description { get; init; }

    /// <summary>邏輯檔案清單</summary>
    public List<BackupLogicalFile> LogicalFiles { get; init; } = [];
}

/// <summary>
/// 備份中的邏輯檔案
/// </summary>
public class BackupLogicalFile
{
    public string LogicalName { get; init; } = string.Empty;
    public string PhysicalName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty; // D=Data, L=Log
    public long SizeBytes { get; init; }
}
```

---

## 四、Infrastructure 層設計

### 4.1 新增檔案清單

```
src/TableSpec.Infrastructure/
└── Services/
    └── MssqlBackupService.cs
```

### 4.2 MssqlBackupService 實作

```csharp
namespace TableSpec.Infrastructure.Services;

/// <summary>
/// MSSQL 備份服務實作
/// </summary>
public class MssqlBackupService : IBackupService
{
    private readonly string _historyFilePath;
    private BackupHistory? _cachedHistory;

    public MssqlBackupService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TableSpec");
        Directory.CreateDirectory(appDataPath);
        _historyFilePath = Path.Combine(appDataPath, "backup-history.json");
    }

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
        var backupSql = backupType switch
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

        var backupName = $"{databaseName}-{backupType}-{DateTime.Now:yyyyMMdd-HHmmss}";

        // 註冊進度事件
        connection.InfoMessage += (sender, e) =>
        {
            if (e.Message.Contains("percent"))
            {
                // 解析進度百分比
                var match = System.Text.RegularExpressions.Regex
                    .Match(e.Message, @"(\d+)\s*percent");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                {
                    progress?.Report(new BackupProgress
                    {
                        PercentComplete = percent,
                        Message = $"備份進度: {percent}%"
                    });
                }
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

        await command.ExecuteNonQueryAsync(cancellationToken);

        progress?.Report(new BackupProgress
        {
            PercentComplete = 100,
            Message = "備份完成"
        });

        // 取得檔案大小
        var fileInfo = new FileInfo(backupPath);
        var fileSize = fileInfo.Exists ? fileInfo.Length : 0;

        // 取得 SQL Server 版本
        await using var versionCommand = new SqlCommand("SELECT @@VERSION", connection);
        var versionResult = await versionCommand.ExecuteScalarAsync(cancellationToken);
        var sqlVersion = versionResult?.ToString()?.Split('\n').FirstOrDefault() ?? "";

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

        // 取得備份檔案資訊以獲取原始資料庫名稱
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
            try
            {
                await using var setSingleUserCommand = new SqlCommand($@"
                    IF EXISTS (SELECT 1 FROM sys.databases WHERE name = @DbName)
                    BEGIN
                        ALTER DATABASE [{targetDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    END", connection);
                setSingleUserCommand.Parameters.AddWithValue("@DbName", targetDatabaseName);
                await setSingleUserCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // 資料庫可能不存在，忽略錯誤
            }
        }

        // 建立還原 SQL
        var restoreSql = new StringBuilder();
        restoreSql.AppendLine($"RESTORE DATABASE [{targetDatabaseName}]");
        restoreSql.AppendLine($"FROM DISK = @BackupPath");
        restoreSql.AppendLine("WITH");

        if (options.WithReplace)
        {
            restoreSql.AppendLine("    REPLACE,");
        }

        // 處理檔案重新配置（新資料庫時）
        if (options.Mode == RestoreMode.CreateNew)
        {
            foreach (var file in fileInfo.LogicalFiles)
            {
                var newPhysicalPath = file.Type == "D"
                    ? options.DataFilePath ?? GetDefaultDataPath(connection, targetDatabaseName, file.LogicalName)
                    : options.LogFilePath ?? GetDefaultLogPath(connection, targetDatabaseName, file.LogicalName);

                restoreSql.AppendLine($"    MOVE '{file.LogicalName}' TO '{newPhysicalPath}',");
            }
        }

        restoreSql.AppendLine(options.WithRecovery ? "    RECOVERY," : "    NORECOVERY,");
        restoreSql.AppendLine("    STATS = 10");

        // 註冊進度事件
        connection.InfoMessage += (sender, e) =>
        {
            if (e.Message.Contains("percent"))
            {
                var match = System.Text.RegularExpressions.Regex
                    .Match(e.Message, @"(\d+)\s*percent");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                {
                    progress?.Report(new RestoreProgress
                    {
                        PercentComplete = percent,
                        Message = $"還原進度: {percent}%"
                    });
                }
            }
        };

        // 執行還原
        await using var restoreCommand = new SqlCommand(restoreSql.ToString(), connection);
        restoreCommand.CommandTimeout = 0;
        restoreCommand.Parameters.AddWithValue("@BackupPath", backupPath);
        await restoreCommand.ExecuteNonQueryAsync(cancellationToken);

        // 還原後設定為多使用者模式
        if (options.Mode == RestoreMode.OverwriteExisting)
        {
            await using var setMultiUserCommand = new SqlCommand($@"
                ALTER DATABASE [{targetDatabaseName}] SET MULTI_USER;", connection);
            await setMultiUserCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        progress?.Report(new RestoreProgress
        {
            PercentComplete = 100,
            Message = "還原完成"
        });
    }

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

    public async Task<BackupFileInfo> GetBackupFileInfoAsync(
        string connectionString,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // 取得備份標頭資訊
        await using var headerCommand = new SqlCommand(
            "RESTORE HEADERONLY FROM DISK = @BackupPath", connection);
        headerCommand.Parameters.AddWithValue("@BackupPath", backupPath);

        var headerTable = new DataTable();
        using (var adapter = new SqlDataAdapter(headerCommand))
        {
            adapter.Fill(headerTable);
        }

        var headerRow = headerTable.Rows[0];

        // 取得檔案清單
        await using var fileListCommand = new SqlCommand(
            "RESTORE FILELISTONLY FROM DISK = @BackupPath", connection);
        fileListCommand.Parameters.AddWithValue("@BackupPath", backupPath);

        var fileListTable = new DataTable();
        using (var adapter = new SqlDataAdapter(fileListCommand))
        {
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

        var backupType = headerRow.Field<byte>("BackupType") switch
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
            SqlServerVersion = headerRow.Field<string>("SoftwareVersionMajor")?.ToString() ?? "",
            DatabaseVersion = headerRow.Field<int>("DatabaseVersion"),
            Description = headerRow.Field<string>("BackupDescription"),
            LogicalFiles = logicalFiles
        };
    }

    // ... 其他方法（歷史記錄管理、輔助方法）
}
```

---

## 五、Desktop 層設計

### 5.1 新增檔案清單

```
src/TableSpec.Desktop/
├── ViewModels/
│   └── BackupRestoreDocumentViewModel.cs
├── Views/
│   ├── BackupRestoreDocumentView.axaml
│   └── BackupRestoreDocumentView.axaml.cs
└── Converters/
    └── FileSizeConverter.cs
```

### 5.2 UI 設計

```
┌───────────────────────────────────────────────────────────────────────────┐
│ [工具列]                                                                   │
│  [備份] [還原] [驗證] [重新整理歷史]                                        │
├───────────────────────────────────────────────────────────────────────────┤
│ [分頁: 備份 | 還原 | 歷史]                                                  │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ═══════════════════════ 備份分頁 ═══════════════════════                 │
│                                                                           │
│  [來源資料庫]                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ 連線: [客戶A資料庫      ▼]                                           │ │
│  │ 資料庫: CustomerDB                                                   │ │
│  │ 伺服器: 192.168.1.100                                                │ │
│  │ 上次備份: 2026-01-23 14:30 (1 天前)                                  │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
│  [備份設定]                                                                │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ 備份類型: ○ 完整備份  ○ 差異備份  ○ 交易記錄                         │ │
│  │                                                                     │ │
│  │ 備份路徑: [D:\Backups\CustomerDB_20260124.bak    ] [瀏覽...]       │ │
│  │                                                                     │ │
│  │ 備份描述: [Schema Compare 前的備份                ]                  │ │
│  │                                                                     │ │
│  │ [☑] 備份完成後自動驗證                                               │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
│  [開始備份]                                                                │
│                                                                           │
│  [進度]                                                                    │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ ████████████████████████████░░░░░░░░░░  75%                         │ │
│  │ 正在備份資料檔案...                                                  │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ═══════════════════════ 還原分頁 ═══════════════════════                 │
│                                                                           │
│  [備份檔案]                                                                │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ 檔案路徑: [D:\Backups\CustomerDB_20260124.bak    ] [瀏覽...]       │ │
│  │                                                                     │ │
│  │ [驗證備份]  ✅ 備份有效                                              │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
│  [備份資訊]                                                                │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ 來源資料庫: CustomerDB                                               │ │
│  │ 來源伺服器: 192.168.1.100                                            │ │
│  │ 備份時間: 2026-01-24 10:30:00                                        │ │
│  │ 備份類型: 完整備份                                                   │ │
│  │ 檔案大小: 1.25 GB                                                    │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
│  [還原設定]                                                                │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ 目標連線: [客戶A資料庫      ▼]                                       │ │
│  │                                                                     │ │
│  │ 還原模式:                                                            │ │
│  │   ○ 覆蓋原資料庫 (CustomerDB)                                       │ │
│  │   ○ 還原到新資料庫: [CustomerDB_Restored     ]                      │ │
│  │                                                                     │ │
│  │ ⚠️ 警告: 覆蓋原資料庫將會遺失所有現有資料！                          │ │
│  │                                                                     │ │
│  │ [☑] 還原後立即可用 (WITH RECOVERY)                                   │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
│  [開始還原]                                                                │
│                                                                           │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ═══════════════════════ 歷史分頁 ═══════════════════════                 │
│                                                                           │
│  [篩選] 連線: [全部        ▼]    日期: [最近 30 天 ▼]                     │
│                                                                           │
│  ┌───────────────────────────────────────────────────────────────────┐   │
│  │ 時間             │ 連線       │ 資料庫    │ 類型 │ 大小    │ 狀態  │   │
│  ├──────────────────┼────────────┼───────────┼──────┼─────────┼───────┤   │
│  │ 2026-01-24 10:30 │ 客戶A      │ CustomerDB│ 完整 │ 1.25 GB │ ✅    │   │
│  │ 2026-01-23 14:30 │ 客戶A      │ CustomerDB│ 完整 │ 1.20 GB │ ✅    │   │
│  │ 2026-01-22 09:00 │ 客戶B      │ SalesDB   │ 完整 │ 850 MB  │ ✅    │   │
│  │ ...              │            │           │      │         │       │   │
│  └───────────────────────────────────────────────────────────────────┘   │
│                                                                           │
│  [選中的備份]                                                              │
│  [還原此備份] [驗證] [開啟資料夾] [從歷史移除]                             │
│                                                                           │
├───────────────────────────────────────────────────────────────────────────┤
│ [狀態列] 就緒                                                              │
└───────────────────────────────────────────────────────────────────────────┘
```

### 5.3 BackupRestoreDocumentViewModel 設計

```csharp
/// <summary>
/// 備份還原文件 ViewModel（MDI Document）
/// </summary>
public partial class BackupRestoreDocumentViewModel : DocumentViewModel
{
    private readonly IBackupService _backupService;
    private readonly IConnectionManager _connectionManager;

    // === DocumentViewModel 覆寫 ===
    public override string DocumentType => "BackupRestore";
    public override string DocumentKey => DocumentType; // 只允許開啟一個

    // === 連線選擇 ===
    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    // === 備份設定 ===
    [ObservableProperty]
    private BackupType _selectedBackupType = BackupType.Full;

    [ObservableProperty]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    private string _backupDescription = string.Empty;

    [ObservableProperty]
    private bool _verifyAfterBackup = true;

    // === 還原設定 ===
    [ObservableProperty]
    private string _restoreFilePath = string.Empty;

    [ObservableProperty]
    private BackupFileInfo? _restoreFileInfo;

    [ObservableProperty]
    private RestoreMode _selectedRestoreMode = RestoreMode.OverwriteExisting;

    [ObservableProperty]
    private string _newDatabaseName = string.Empty;

    [ObservableProperty]
    private ConnectionProfile? _restoreTargetProfile;

    [ObservableProperty]
    private bool _isBackupValid;

    // === 歷史記錄 ===
    public ObservableCollection<BackupInfo> BackupHistory { get; } = [];

    [ObservableProperty]
    private BackupInfo? _selectedHistoryItem;

    // === 狀態 ===
    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private int _progressPercentage;

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "就緒";

    [ObservableProperty]
    private int _selectedTabIndex;

    // === 建構函式 ===
    public BackupRestoreDocumentViewModel()
    {
        Title = "備份與還原";
        Icon = "💾";
        CanClose = true;
    }

    public BackupRestoreDocumentViewModel(
        IBackupService backupService,
        IConnectionManager connectionManager)
    {
        _backupService = backupService;
        _connectionManager = connectionManager;

        Title = "備份與還原";
        Icon = "💾";
        CanClose = true;

        LoadConnectionProfiles();
        LoadBackupHistory();
    }

    // === 命令 ===
    [RelayCommand]
    private async Task BackupAsync() { /* 執行備份 */ }

    [RelayCommand]
    private async Task RestoreAsync() { /* 執行還原 */ }

    [RelayCommand]
    private async Task VerifyBackupAsync() { /* 驗證備份 */ }

    [RelayCommand]
    private async Task BrowseBackupPathAsync() { /* 選擇備份路徑 */ }

    [RelayCommand]
    private async Task BrowseRestoreFileAsync() { /* 選擇還原檔案 */ }

    [RelayCommand]
    private void RefreshHistory() { /* 重新載入歷史 */ }

    [RelayCommand]
    private void RestoreFromHistory() { /* 從歷史還原 */ }

    [RelayCommand]
    private void OpenBackupFolder() { /* 開啟備份資料夾 */ }

    [RelayCommand]
    private void RemoveFromHistory() { /* 從歷史移除 */ }
}
```

---

## 六、MainWindow 整合

### 6.1 新增選單項目

```xml
<MenuItem Header="工具(_T)">
    <MenuItem Header="SQL 查詢(_S)" Command="{Binding OpenSqlQueryCommand}" ... />
    <MenuItem Header="欄位搜尋(_F)" Command="{Binding OpenColumnSearchCommand}" ... />
    <Separator/>
    <MenuItem Header="備份與還原(_B)" Command="{Binding OpenBackupRestoreCommand}">
        <MenuItem.Icon>
            <TextBlock Text="💾" FontSize="14"/>
        </MenuItem.Icon>
    </MenuItem>
    <MenuItem Header="Schema 比較(_C)" Command="{Binding OpenSchemaCompareCommand}" ... />
</MenuItem>
```

### 6.2 MainWindowViewModel 新增命令

```csharp
[RelayCommand]
private void OpenBackupRestore()
{
    // 檢查是否已開啟
    var existing = Documents.OfType<BackupRestoreDocumentViewModel>().FirstOrDefault();
    if (existing != null)
    {
        SelectedDocument = existing;
        return;
    }

    var doc = App.Services?.GetRequiredService<BackupRestoreDocumentViewModel>()
        ?? new BackupRestoreDocumentViewModel();
    doc.CloseRequested += OnDocumentCloseRequested;
    Documents.Add(doc);
    SelectedDocument = doc;
}
```

### 6.3 DI 註冊

```csharp
// 在 Program.cs 的 ConfigureServices() 中新增
services.AddSingleton<IBackupService, MssqlBackupService>();
services.AddTransient<BackupRestoreDocumentViewModel>();
```

---

## 七、實作步驟

### 階段 1：Domain 層（Day 1）

| 步驟 | 工作內容 |
|------|---------|
| 1.1 | 建立 BackupInfo, BackupHistory, RestoreOptions 實體 |
| 1.2 | 建立 BackupType, RestoreMode 列舉 |
| 1.3 | 建立 IBackupService 介面和相關類別 |

### 階段 2：Infrastructure 層（Day 2-3）

| 步驟 | 工作內容 |
|------|---------|
| 2.1 | 實作 MssqlBackupService.BackupDatabaseAsync |
| 2.2 | 實作 MssqlBackupService.RestoreDatabaseAsync |
| 2.3 | 實作 MssqlBackupService.VerifyBackupAsync |
| 2.4 | 實作備份歷史記錄管理（JSON 持久化） |
| 2.5 | 撰寫整合測試 |

### 階段 3：Desktop 層（Day 4-5）

| 步驟 | 工作內容 |
|------|---------|
| 3.1 | 建立 BackupRestoreDocumentView.axaml |
| 3.2 | 實作 BackupRestoreDocumentViewModel |
| 3.3 | 整合到 MainWindow |
| 3.4 | UI 測試與調整 |

---

## 八、與 Schema Compare 整合

備份/還原功能完成後，Schema Compare 可以：

1. **執行前檢查**：檢查是否有最近 24 小時內的備份
2. **強制備份**：如果沒有最新備份，提示用戶先進行備份
3. **快速備份入口**：在 Schema Compare 執行模式中提供「立即備份」按鈕
4. **還原入口**：如果 Migration 失敗，提供快速還原入口

```csharp
// Schema Compare 執行前檢查
public async Task<bool> ValidateBackupStatusAsync(Guid connectionId)
{
    var history = _backupService.GetBackupHistory();
    var hasRecentBackup = history.HasRecentBackup(connectionId, TimeSpan.FromHours(24));

    if (!hasRecentBackup)
    {
        // 提示用戶需要先備份
        return false;
    }

    return true;
}
```

---

*此文件將隨開發進度持續更新*
