# TableSpec Desktop 應用程式設計文件

**建立日期**: 2026-01-24
**專案目錄**: `C:\Users\zihao\source\repos\DatabaseDescriptionApp`
**原始專案**: WayDoSoft.MoldPlan.TableSpecWeb (ASP.NET Core MVC)

---

## 1. 專案概述

將現有的 ASP.NET Core MVC Web 應用程式轉換為跨平台桌面應用程式。

### 1.1 目標

- 跨平台支援（Windows / Mac / Linux）
- 單一執行檔，簡化部署
- 現代扁平 UI 風格
- 可彈性設定多組資料庫連線

### 1.2 技術決策

| 項目 | 選擇 | 理由 |
|------|------|------|
| UI 框架 | Avalonia UI 11.x | 跨平台、活躍社群、完整 Linux 支援 |
| 架構模式 | Clean Architecture + MVVM | 分層清晰、可測試性高 |
| 開發方法 | TDD | 確保程式碼品質 |
| UI 主題 | Semi.Avalonia | 現代扁平風格 |

---

## 2. 專案結構

```
TableSpecApp/
├── src/
│   ├── TableSpec.Domain/              # 領域層
│   │   ├── Entities/
│   │   │   ├── TableInfo.cs
│   │   │   ├── ColumnInfo.cs
│   │   │   ├── IndexInfo.cs
│   │   │   ├── RelationInfo.cs
│   │   │   └── ConnectionProfile.cs
│   │   └── Interfaces/
│   │       ├── ITableRepository.cs
│   │       ├── IColumnRepository.cs
│   │       ├── IIndexRepository.cs
│   │       └── IRelationRepository.cs
│   │
│   ├── TableSpec.Application/         # 應用層
│   │   ├── Services/
│   │   │   ├── ITableQueryService.cs
│   │   │   ├── TableQueryService.cs
│   │   │   ├── IConnectionManager.cs
│   │   │   └── IExportService.cs
│   │   └── DTOs/
│   │
│   ├── TableSpec.Infrastructure/      # 基礎設施層
│   │   ├── Repositories/
│   │   │   ├── TableRepository.cs
│   │   │   ├── ColumnRepository.cs
│   │   │   ├── IndexRepository.cs
│   │   │   └── RelationRepository.cs
│   │   └── Services/
│   │       ├── ConnectionManager.cs
│   │       └── ExcelExportService.cs
│   │
│   └── TableSpec.Desktop/             # 展示層 (Avalonia)
│       ├── App.axaml
│       ├── Program.cs
│       ├── ViewModels/
│       │   ├── ViewModelBase.cs
│       │   ├── MainViewModel.cs
│       │   ├── ConnectionSetupViewModel.cs
│       │   ├── ObjectTreeViewModel.cs
│       │   ├── TableDetailViewModel.cs
│       │   └── ColumnsViewModel.cs
│       ├── Views/
│       │   ├── MainWindow.axaml
│       │   ├── ConnectionSetupWindow.axaml
│       │   ├── ObjectTreeView.axaml
│       │   └── TableDetailView.axaml
│       └── Services/
│           └── ViewLocator.cs
│
└── tests/
    ├── TableSpec.Domain.Tests/
    ├── TableSpec.Application.Tests/
    └── TableSpec.Infrastructure.Tests/
```

---

## 3. 技術棧

### 3.1 核心套件

| 層級 | 套件 | 版本 | 用途 |
|------|------|------|------|
| Desktop | Avalonia.Desktop | 11.x | 跨平台 UI |
| Desktop | Semi.Avalonia | latest | 現代 UI 主題 |
| Desktop | CommunityToolkit.Mvvm | 8.x | MVVM 工具 |
| All | Microsoft.Extensions.DependencyInjection | 8.x | DI 容器 |
| Infrastructure | Microsoft.Data.SqlClient | 5.x | SQL Server 連線 |
| Infrastructure | Dapper | 2.x | 輕量 ORM |
| Infrastructure | ClosedXML | 0.104.x | Excel 匯出 |

### 3.2 測試套件

| 套件 | 用途 |
|------|------|
| xUnit | 測試框架 |
| NSubstitute | Mock 框架 |
| FluentAssertions | 斷言語法 |

---

## 4. Domain 層設計

### 4.1 實體定義

```csharp
// TableInfo.cs
public class TableInfo
{
    public string Type { get; init; }        // TABLE, VIEW, PROCEDURE, FUNCTION
    public string Schema { get; init; }
    public string Name { get; init; }
    public string? Description { get; init; }
}

// ColumnInfo.cs
public class ColumnInfo
{
    public string Schema { get; init; }
    public string TableName { get; init; }
    public string ColumnName { get; init; }
    public string DataType { get; init; }
    public int? Length { get; init; }
    public string? DefaultValue { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsUniqueKey { get; init; }
    public bool IsIndexed { get; init; }
    public bool IsNullable { get; init; }
    public string? Description { get; init; }
}

// IndexInfo.cs
public class IndexInfo
{
    public string Name { get; init; }
    public string Type { get; init; }           // Clustered, NonClustered
    public IReadOnlyList<string> Columns { get; init; }
    public bool IsUnique { get; init; }
    public bool IsPrimaryKey { get; init; }
}

// RelationInfo.cs
public class RelationInfo
{
    public string ConstraintName { get; init; }
    public string FromTable { get; init; }
    public string FromColumn { get; init; }
    public string ToTable { get; init; }
    public string ToColumn { get; init; }
    public RelationType Type { get; init; }
}

public enum RelationType
{
    Outgoing,  // 本表參考其他表 (FK)
    Incoming   // 其他表參考本表
}

// ConnectionProfile.cs
public class ConnectionProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; }
    public string Server { get; set; }
    public string Database { get; set; }
    public AuthenticationType AuthType { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool IsDefault { get; set; }
}

public enum AuthenticationType
{
    WindowsAuthentication,
    SqlServerAuthentication
}
```

### 4.2 Repository 介面

```csharp
public interface ITableRepository
{
    Task<IReadOnlyList<TableInfo>> GetAllTablesAsync(CancellationToken ct = default);
}

public interface IColumnRepository
{
    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(
        string type, string schema, string tableName,
        CancellationToken ct = default);
}

public interface IIndexRepository
{
    Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(
        string schema, string tableName,
        CancellationToken ct = default);
}

public interface IRelationRepository
{
    Task<IReadOnlyList<RelationInfo>> GetRelationsAsync(
        string schema, string tableName,
        CancellationToken ct = default);
}
```

---

## 5. Application 層設計

### 5.1 服務介面

```csharp
public interface ITableQueryService
{
    Task<IReadOnlyList<TableInfo>> GetAllTablesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ColumnInfo>> GetTableColumnsAsync(
        string type, string schema, string tableName,
        CancellationToken ct = default);
    Task<IReadOnlyList<IndexInfo>> GetTableIndexesAsync(
        string schema, string tableName,
        CancellationToken ct = default);
    Task<IReadOnlyList<RelationInfo>> GetTableRelationsAsync(
        string schema, string tableName,
        CancellationToken ct = default);
}

public interface IConnectionManager
{
    IReadOnlyList<ConnectionProfile> GetAllProfiles();
    ConnectionProfile? GetCurrentProfile();
    void SetCurrentProfile(Guid profileId);
    void AddProfile(ConnectionProfile profile);
    void UpdateProfile(ConnectionProfile profile);
    void DeleteProfile(Guid profileId);
    Task<bool> TestConnectionAsync(ConnectionProfile profile);
    string BuildConnectionString(ConnectionProfile profile);
}

public interface IExportService
{
    Task<byte[]> ExportToExcelAsync(CancellationToken ct = default);
}
```

---

## 6. UI 設計

### 6.1 主視窗佈局

```
┌─────────────────────────────────────────────────────────┐
│  TableSpec    [開發環境 ▼]    [匯出 Excel]   [設定]    │
├───────────────┬─────────────────────────────────────────┤
│               │                                         │
│ [搜尋物件...] │   [搜尋欄位...]                        │
│               │  ┌─────────────────────────────────────┐│
│  Tables(25)   │  │ [欄位]  [索引]  [關聯]              ││
│    ├ Users    │  ├─────────────────────────────────────┤│
│    ├ Orders   │  │ 欄位      型別       說明          ││
│    └ ...      │  │ Id        int        主鍵          ││
│               │  │ Name      nvarchar   名稱          ││
│  Views(5)     │  └─────────────────────────────────────┘│
│    └ ...      │                                         │
│               │   [顯示範例資料]                        │
│  SPs(10)      │                                         │
│    └ ...      │                                         │
│               │                                         │
│  Functions(3) │                                         │
│    └ ...      │                                         │
│               │                                         │
├───────────────┴─────────────────────────────────────────┤
│  共 43 個物件 │ 已選擇: dbo.Users (TABLE)              │
└─────────────────────────────────────────────────────────┘
```

### 6.2 連線設定視窗

```
┌─────────────────────────────────────────────┐
│  資料庫連線設定                       [X]   │
├─────────────────────────────────────────────┤
│                                             │
│  連線名稱: [開發環境            ]           │
│  伺服器:   [localhost           ]           │
│  資料庫:   [MoldPlan            ]           │
│                                             │
│  驗證方式:                                  │
│  ○ Windows 驗證                             │
│  ● SQL Server 驗證                          │
│    帳號: [sa                    ]           │
│    密碼: [********              ]           │
│                                             │
│  ☐ 設為預設連線                             │
│                                             │
│       [測試連線]    [取消]    [儲存]        │
└─────────────────────────────────────────────┘
```

### 6.3 導航功能

- 左側樹狀結構：按類型分組（Tables / Views / SPs / Functions）
- 即時搜尋過濾（物件名稱、欄位名稱）
- 點擊物件顯示詳細資訊
- 詳細資訊分頁（欄位 / 索引 / 關聯）

---

## 7. 連線管理

### 7.1 儲存位置

- Windows: `%APPDATA%/TableSpec/connections.json`
- Mac: `~/Library/Application Support/TableSpec/connections.json`
- Linux: `~/.config/TableSpec/connections.json`

### 7.2 安全性

- 密碼使用平台原生加密
  - Windows: DPAPI
  - Mac: Keychain
  - Linux: libsecret

---

## 8. 測試策略

### 8.1 測試範圍

| 層級 | 測試類型 | 重點 |
|------|----------|------|
| Domain | 單元測試 | 實體驗證邏輯 |
| Application | 單元測試 | Service 業務邏輯 |
| Infrastructure | 整合測試 | SQL 查詢正確性 |
| Desktop | 單元測試 | ViewModel 狀態變化 |

### 8.2 TDD 流程

1. Red - 先寫失敗的測試
2. Green - 寫最小實作讓測試通過
3. Refactor - 重構優化

---

## 9. 實作順序

1. **Phase 1: 專案建置**
   - 建立 Solution 和各專案
   - 設定套件參考和專案參考

2. **Phase 2: Domain 層**
   - 實作實體類別
   - 定義 Repository 介面

3. **Phase 3: Infrastructure 層**
   - 實作 Repository（移植現有 SQL）
   - 實作 ConnectionManager

4. **Phase 4: Application 層**
   - 實作 Service 類別
   - 實作 ExportService

5. **Phase 5: Desktop 層**
   - 建立主視窗框架
   - 實作連線設定視窗
   - 實作物件瀏覽功能
   - 實作詳細資訊顯示

6. **Phase 6: 測試與優化**
   - 補齊測試覆蓋
   - UI 優化與調整
   - 打包發布設定

---

## 10. 附註

- 現有 Repository SQL 查詢邏輯可直接移植
- Excel 匯出功能沿用 ClosedXML 實作
- 未來可擴展支援其他資料庫（PostgreSQL、MySQL）
