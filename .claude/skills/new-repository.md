---
description: 建立新的 Repository（介面 + 實作）
---

# 建立新的 Repository

同時建立 Domain 層介面和 Infrastructure 層實作。

## 參數

- `$ARGUMENTS` - Repository 名稱（例如：`Trigger`，會產生 `ITriggerRepository`）

## 步驟

1. 確認 Repository 名稱，如果未提供則詢問使用者

2. 在 `src/TableSpec.Domain/Interfaces/` 建立 `I{Name}Repository.cs`：

```csharp
using TableSpec.Domain.Entities;

namespace TableSpec.Domain.Interfaces;

/// <summary>
/// {Name} Repository 介面
/// </summary>
public interface I{Name}Repository
{
    Task<IReadOnlyList<{Name}Info>> GetAllAsync(CancellationToken ct = default);
}
```

3. 在 `src/TableSpec.Infrastructure/Repositories/` 建立 `{Name}Repository.cs`：

```csharp
using Dapper;
using Microsoft.Data.SqlClient;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Infrastructure.Repositories;

/// <summary>
/// {Name} Repository 實作
/// </summary>
public class {Name}Repository : I{Name}Repository
{
    private readonly Func<string?> _connectionStringProvider;

    public {Name}Repository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<{Name}Info>> GetAllAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<{Name}Info>();

        const string sql = @"
            -- TODO: 實作 SQL 查詢
            SELECT 1";

        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<{Name}Info>(sql);
        return result.ToList();
    }
}
```

4. 提醒使用者在 `Program.cs` 註冊 DI：

```csharp
services.AddSingleton<I{Name}Repository>(sp =>
    new {Name}Repository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
```

## 規範

- 介面放在 Domain 層，實作放在 Infrastructure 層
- 使用 `Func<string?>` 連線字串工廠模式
- 使用 Dapper 進行資料存取
- 方法支援 CancellationToken
