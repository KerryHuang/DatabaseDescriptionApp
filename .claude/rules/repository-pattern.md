# Repository 模式規範

本專案使用 Repository 模式進行資料存取。

## 介面定義 (Domain 層)

Repository 介面定義於 `TableSpec.Domain/Interfaces/`：

```csharp
public interface ITableRepository
{
    Task<IReadOnlyList<TableInfo>> GetAllAsync(CancellationToken ct = default);
}
```

## 實作 (Infrastructure 層)

Repository 實作於 `TableSpec.Infrastructure/Repositories/`：

```csharp
public class TableRepository : ITableRepository
{
    private readonly Func<string?> _connectionStringProvider;

    public TableRepository(Func<string?> connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<TableInfo>> GetAllAsync(CancellationToken ct = default)
    {
        var connectionString = _connectionStringProvider();
        if (string.IsNullOrEmpty(connectionString))
            return Array.Empty<TableInfo>();

        // 使用 Dapper 查詢
        await using var connection = new SqlConnection(connectionString);
        var result = await connection.QueryAsync<TableInfo>(sql);
        return result.ToList();
    }
}
```

## 連線字串工廠模式

Repository 不直接持有連線字串，而是透過 `Func<string?>` 委派動態取得：

```csharp
services.AddSingleton<ITableRepository>(sp =>
    new TableRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
```

這樣可以支援執行時切換資料庫連線。

## SQL 查詢規範

- 使用 Dapper 進行資料存取
- SQL 查詢使用 `const string` 或 `string` 變數
- 複雜查詢可使用多行字串 `@"..."`
- 參數化查詢防止 SQL Injection
