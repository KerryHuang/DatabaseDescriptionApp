---
paths:
  - "**/*Repository.cs"
  - "**/*RepositoryTests.cs"
  - "**/Repositories/**"
---

# Repository 模式規範

本專案使用 Repository 模式進行資料存取。

## 分層規則

- **介面**定義於 `Specurai.Domain/Interfaces/`（Domain 層）
- **實作**定義於 `Specurai.Infrastructure/Repositories/`（Infrastructure 層）

## 連線字串工廠模式

Repository 透過 `Func<string?>` 委派動態取得連線字串，支援執行時切換資料庫。

DI 註冊範例：
```csharp
services.AddSingleton<ITableRepository>(sp =>
    new TableRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
```

## SQL 查詢規範

- 使用 **Dapper** 進行資料存取
- SQL 查詢使用 `const string` 或多行字串 `@"..."`
- 參數化查詢防止 SQL Injection（禁止字串串接）
- DMV 查詢加 `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED` 避免鎖定
