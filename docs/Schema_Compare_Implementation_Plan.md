# Schema Compare 功能實作計畫

> 建立日期：2026-01-24
> 狀態：規劃中

---

## 一、專案概述

### 1.1 目標

在現有 TableSpec 專案中新增「多資料庫結構比較與同步」功能，實現：

1. **結構收集**：從多個已設定的連線中收集完整 Schema 資訊
2. **差異分析**：比較所有資料庫，產生差異矩陣（清晰呈現每個差異點）
3. **標準結構建議**：根據最大化原則產生建議的標準結構
4. **解決方案提供**：為每個差異提供多種解決方式供用戶選擇
5. **風險評估**：評估每個變更的風險等級，高風險項目需特別確認
6. **嚴格驗證**：執行前後的完整驗證機制，確保資料安全
7. **報告產出**：UI 視覺化 + Excel 匯出 + SQL 腳本

### 1.2 功能範圍

| 功能模組 | 說明 |
|---------|------|
| 差異分析報告 | 樹狀圖導航 + 矩陣表格詳情，清晰呈現每個差異 |
| 解決方案選擇 | 為每個差異提供可選的解決方式 |
| Migration 腳本 | 產生 SQL 腳本供人工審核，或在工具內直接執行 |
| 執行前驗證 | 環境檢查、備份驗證、相依性分析、風險評分、測試環境驗證 |
| 執行後驗證 | 結構比對、資料完整性檢查、Rollback 準備 |

### 1.3 安全原則

> ⚠️ **重要**：此功能會影響現有資料庫結構和資料，必須遵守以下原則：

1. **強制備份**：執行任何變更前必須完成資料庫備份
2. **測試優先**：高風險變更必須先在測試環境驗證
3. **人工確認**：所有變更需經過人工審核確認
4. **可回滾**：每個變更都必須有對應的 Rollback 腳本
5. **零高風險錯誤**：不允許產生可能導致資料遺失的錯誤

### 1.4 比較範圍

| 物件類型 | 比較項目 |
|---------|---------|
| Tables | 存在性、欄位、約束、索引 |
| Columns | 名稱、型別、長度、精度、Nullable、Default、Identity、Collation |
| Primary Keys | 名稱、欄位組成 |
| Foreign Keys | 名稱、參照表、參照欄位、ON DELETE/UPDATE 規則 |
| Unique Constraints | 名稱、欄位組成 |
| Check Constraints | 名稱、定義 |
| Indexes | 名稱、類型、欄位、Include Columns、Filter |
| Views | 存在性、定義差異 |
| Stored Procedures | 存在性、定義差異 |
| Functions | 存在性、定義差異 |
| Triggers | 存在性、定義差異 |

### 1.5 風險評估分級

每個變更操作都會被評估風險等級，用於決定執行策略和驗證要求：

| 風險等級 | 顏色 | 操作類型 | 執行要求 |
|---------|------|---------|---------|
| 🟢 **低風險** | 綠色 | 新增 Nullable 欄位、延長 varchar 長度、新增索引 | 可直接執行 |
| 🟡 **中風險** | 黃色 | 修改 Nullable、新增 NOT NULL 欄位（有 Default）、新增約束 | 需確認 |
| 🔴 **高風險** | 紅色 | 縮短欄位長度、修改資料型別、刪除欄位/表格 | 強制測試環境驗證 |
| ⛔ **禁止** | 黑色 | 可能導致資料遺失且無法還原的操作 | 阻止執行 |

#### 風險評分規則

```
總風險分數 = Σ(每個變更的風險分數)

單項風險分數:
- 低風險操作: 1 分
- 中風險操作: 5 分
- 高風險操作: 20 分
- 禁止操作: ∞ (阻止執行)

執行門檻:
- 總分 < 50: 可直接執行（仍需確認）
- 總分 50-100: 需要額外確認
- 總分 > 100: 強制分批執行
- 包含高風險: 強制測試環境先行
```

### 1.6 驗證機制

#### 1.6.1 執行前驗證 (Pre-flight Check)

| 檢查項目 | 說明 | 失敗處理 |
|---------|------|---------|
| **連線狀態** | 驗證所有目標資料庫可連線 | 阻止執行 |
| **權限驗證** | 確認有 ALTER、CREATE、DROP 權限 | 阻止執行 |
| **磁碟空間** | 檢查目標伺服器磁碟空間 | 警告 |
| **備份狀態** | 強制要求有最新備份（24 小時內） | 阻止執行 |
| **相依性分析** | 分析 FK、View、SP 相依關係 | 顯示警告 |
| **資料影響評估** | 評估受影響的資料筆數 | 顯示報告 |
| **風險評分** | 計算總風險分數 | 依分數決定策略 |
| **測試環境驗證** | 高風險變更必須先在測試環境執行 | 阻止正式執行 |

#### 1.6.2 執行後驗證 (Post-execution Validation)

| 驗證項目 | 說明 | 失敗處理 |
|---------|------|---------|
| **結構比對** | 比對執行後的結構是否符合預期 | 觸發告警 |
| **資料完整性** | FK 完整性、關鍵資料抽樣 | 觸發告警 |
| **應用程式測試** | 提供測試清單供人工驗證 | 記錄結果 |
| **Rollback 可用性** | 驗證 Rollback 腳本可執行 | 警告 |

#### 1.6.3 Rollback 機制

```
每個變更都會產生對應的 Rollback 腳本：

ALTER TABLE:
  執行: ALTER TABLE [dbo].[Users] ALTER COLUMN [Email] NVARCHAR(100)
  回滾: ALTER TABLE [dbo].[Users] ALTER COLUMN [Email] VARCHAR(50)

ADD COLUMN:
  執行: ALTER TABLE [dbo].[Users] ADD [Phone] NVARCHAR(20) NULL
  回滾: ALTER TABLE [dbo].[Users] DROP COLUMN [Phone]

DROP COLUMN:
  執行: (產生完整資料備份 SQL) + ALTER TABLE ... DROP COLUMN
  回滾: ALTER TABLE ... ADD COLUMN + (資料還原 SQL)
```

---

## 二、架構設計

### 2.1 整體架構圖

```
┌─────────────────────────────────────────────────────────────────┐
│                        Desktop 層 (MDI 架構)                     │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  MainWindow.axaml (TabControl 容器)                      │   │
│  │  └── SchemaCompareDocumentView.axaml (UserControl)       │   │
│  │       └── SchemaCompareDocumentViewModel                 │   │
│  │            ├── 選擇要比較的連線（多選 CheckBox）          │   │
│  │            ├── 顯示差異樹狀圖                             │   │
│  │            ├── 衝突解決 UI                                │   │
│  │            └── 匯出報告                                   │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Application 層                              │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  ISchemaCompareService                                   │   │
│  │  ├── CollectSchemasAsync(connectionIds[])               │   │
│  │  ├── CompareAsync(schemas[])                            │   │
│  │  └── GenerateStandardSchemaAsync(comparison, resolutions)│   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  ISchemaReportService                                    │   │
│  │  ├── ExportToExcelAsync(comparison)                     │   │
│  │  └── GenerateSummaryAsync(comparison)                   │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Infrastructure 層                            │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  ISchemaCollector (介面定義於 Domain)                    │   │
│  │  └── MssqlSchemaCollector                               │   │
│  │       └── 收集完整 Schema 到 DatabaseSchema 實體         │   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  SchemaCompareExcelExporter                              │   │
│  │  └── 產生差異比較 Excel 報告                             │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        Domain 層                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Entities (新增)                                         │   │
│  │  ├── DatabaseSchema          # 單一資料庫完整快照        │   │
│  │  ├── SchemaTable             # 表格詳細資訊              │   │
│  │  ├── SchemaColumn            # 欄位詳細資訊              │   │
│  │  ├── SchemaIndex             # 索引詳細資訊              │   │
│  │  ├── SchemaConstraint        # 約束詳細資訊              │   │
│  │  ├── SchemaView              # View 定義                 │   │
│  │  ├── SchemaProcedure         # SP 定義                   │   │
│  │  ├── SchemaFunction          # Function 定義             │   │
│  │  ├── SchemaTrigger           # Trigger 定義              │   │
│  │  ├── SchemaComparison        # 比較結果                  │   │
│  │  ├── SchemaDifference        # 單一差異項                │   │
│  │  ├── SchemaConflict          # 需人工決定的衝突          │   │
│  │  └── ConflictResolution      # 衝突解決方案              │   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Interfaces (新增)                                       │   │
│  │  └── ISchemaCollector                                   │   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Enums (新增)                                            │   │
│  │  ├── DifferenceType          # 新增/遺漏/修改            │   │
│  │  ├── DifferenceCategory      # Table/Column/Index/...   │   │
│  │  ├── ConflictType            # 型別不同/Default不同/...  │   │
│  │  └── ConflictSeverity        # 高/中/低                  │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 資料流程圖

```
使用者選擇連線
       │
       ▼
┌──────────────┐
│ 收集 Schema  │ ─── 對每個連線執行 SQL 查詢
└──────────────┘
       │
       ▼
┌──────────────┐
│ 產生快照     │ ─── DatabaseSchema[]
└──────────────┘
       │
       ▼
┌──────────────┐
│ 執行比較     │ ─── 物件對物件、屬性對屬性比較
└──────────────┘
       │
       ▼
┌──────────────┐
│ 產生差異清單 │ ─── SchemaDifference[]
└──────────────┘
       │
       ▼
┌──────────────┐
│ 識別衝突     │ ─── 無法自動決定的標記為 SchemaConflict
└──────────────┘
       │
       ▼
┌──────────────┐
│ 顯示 UI      │ ─── 樹狀差異 + 衝突清單
└──────────────┘
       │
       ▼
┌──────────────┐
│ 人工解決衝突 │ ─── 使用者選擇採用哪個版本
└──────────────┘
       │
       ▼
┌──────────────┐
│ 匯出報告     │ ─── Excel 差異報告
└──────────────┘
```

---

## 三、Domain 層設計

### 3.1 新增檔案清單

```
src/TableSpec.Domain/
├── Entities/
│   └── SchemaCompare/
│       ├── DatabaseSchema.cs
│       ├── SchemaTable.cs
│       ├── SchemaColumn.cs
│       ├── SchemaIndex.cs
│       ├── SchemaConstraint.cs
│       ├── SchemaView.cs
│       ├── SchemaProcedure.cs
│       ├── SchemaFunction.cs
│       ├── SchemaTrigger.cs
│       ├── SchemaComparison.cs
│       ├── SchemaDifference.cs
│       ├── SchemaConflict.cs
│       └── ConflictResolution.cs
├── Enums/
│   ├── DifferenceType.cs
│   ├── DifferenceCategory.cs
│   ├── ConflictType.cs
│   └── ConflictSeverity.cs
└── Interfaces/
    └── ISchemaCollector.cs
```

### 3.2 核心實體設計

#### DatabaseSchema.cs

```csharp
namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 單一資料庫的完整 Schema 快照
/// </summary>
public class DatabaseSchema
{
    /// <summary>連線設定檔 ID</summary>
    public Guid ConnectionId { get; init; }

    /// <summary>連線名稱（顯示用）</summary>
    public string ConnectionName { get; init; } = string.Empty;

    /// <summary>資料庫名稱</summary>
    public string DatabaseName { get; init; } = string.Empty;

    /// <summary>伺服器名稱</summary>
    public string ServerName { get; init; } = string.Empty;

    /// <summary>收集時間</summary>
    public DateTime CollectedAt { get; init; }

    /// <summary>SQL Server 版本</summary>
    public string SqlServerVersion { get; init; } = string.Empty;

    /// <summary>資料庫 Collation</summary>
    public string Collation { get; init; } = string.Empty;

    /// <summary>所有資料表</summary>
    public IReadOnlyList<SchemaTable> Tables { get; init; } = [];

    /// <summary>所有檢視</summary>
    public IReadOnlyList<SchemaView> Views { get; init; } = [];

    /// <summary>所有預存程序</summary>
    public IReadOnlyList<SchemaProcedure> Procedures { get; init; } = [];

    /// <summary>所有函數</summary>
    public IReadOnlyList<SchemaFunction> Functions { get; init; } = [];

    /// <summary>所有觸發程序</summary>
    public IReadOnlyList<SchemaTrigger> Triggers { get; init; } = [];
}
```

#### SchemaTable.cs

```csharp
namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 資料表完整結構
/// </summary>
public class SchemaTable
{
    /// <summary>Schema 名稱（如 dbo）</summary>
    public string SchemaName { get; init; } = "dbo";

    /// <summary>資料表名稱</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>完整名稱（Schema.Table）</summary>
    public string FullName => $"{SchemaName}.{TableName}";

    /// <summary>資料表描述</summary>
    public string? Description { get; init; }

    /// <summary>所有欄位</summary>
    public IReadOnlyList<SchemaColumn> Columns { get; init; } = [];

    /// <summary>主鍵約束</summary>
    public SchemaConstraint? PrimaryKey { get; init; }

    /// <summary>外鍵約束</summary>
    public IReadOnlyList<SchemaConstraint> ForeignKeys { get; init; } = [];

    /// <summary>唯一約束</summary>
    public IReadOnlyList<SchemaConstraint> UniqueConstraints { get; init; } = [];

    /// <summary>檢查約束</summary>
    public IReadOnlyList<SchemaConstraint> CheckConstraints { get; init; } = [];

    /// <summary>預設約束</summary>
    public IReadOnlyList<SchemaConstraint> DefaultConstraints { get; init; } = [];

    /// <summary>索引</summary>
    public IReadOnlyList<SchemaIndex> Indexes { get; init; } = [];
}
```

#### SchemaColumn.cs

```csharp
namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 欄位完整資訊
/// </summary>
public class SchemaColumn
{
    /// <summary>欄位名稱</summary>
    public string ColumnName { get; init; } = string.Empty;

    /// <summary>欄位順序（1-based）</summary>
    public int OrdinalPosition { get; init; }

    /// <summary>資料型別（如 varchar, int, decimal）</summary>
    public string DataType { get; init; } = string.Empty;

    /// <summary>最大長度（字元/位元組）</summary>
    public int? MaxLength { get; init; }

    /// <summary>數值精度</summary>
    public int? NumericPrecision { get; init; }

    /// <summary>小數位數</summary>
    public int? NumericScale { get; init; }

    /// <summary>是否允許 NULL</summary>
    public bool IsNullable { get; init; }

    /// <summary>預設值定義</summary>
    public string? DefaultValue { get; init; }

    /// <summary>是否為 Identity</summary>
    public bool IsIdentity { get; init; }

    /// <summary>Identity Seed</summary>
    public long? IdentitySeed { get; init; }

    /// <summary>Identity Increment</summary>
    public int? IdentityIncrement { get; init; }

    /// <summary>是否為計算欄位</summary>
    public bool IsComputed { get; init; }

    /// <summary>計算欄位定義</summary>
    public string? ComputedDefinition { get; init; }

    /// <summary>欄位 Collation</summary>
    public string? Collation { get; init; }

    /// <summary>欄位描述</summary>
    public string? Description { get; init; }

    /// <summary>
    /// 取得完整型別描述（如 varchar(50), decimal(18,2)）
    /// </summary>
    public string FullDataType
    {
        get
        {
            var type = DataType.ToUpperInvariant();
            return type switch
            {
                "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" or "BINARY" or "VARBINARY"
                    => MaxLength == -1 ? $"{DataType}(MAX)" : $"{DataType}({MaxLength})",
                "DECIMAL" or "NUMERIC"
                    => $"{DataType}({NumericPrecision},{NumericScale})",
                "DATETIME2" or "DATETIMEOFFSET" or "TIME"
                    => NumericScale.HasValue ? $"{DataType}({NumericScale})" : DataType,
                _ => DataType
            };
        }
    }
}
```

#### SchemaConstraint.cs

```csharp
namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 約束資訊
/// </summary>
public class SchemaConstraint
{
    /// <summary>約束名稱</summary>
    public string ConstraintName { get; init; } = string.Empty;

    /// <summary>約束類型</summary>
    public ConstraintType ConstraintType { get; init; }

    /// <summary>包含的欄位（主鍵/唯一/外鍵）</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>參照的表格（外鍵用）</summary>
    public string? ReferencedTable { get; init; }

    /// <summary>參照的欄位（外鍵用）</summary>
    public IReadOnlyList<string> ReferencedColumns { get; init; } = [];

    /// <summary>ON DELETE 規則（外鍵用）</summary>
    public string? OnDeleteAction { get; init; }

    /// <summary>ON UPDATE 規則（外鍵用）</summary>
    public string? OnUpdateAction { get; init; }

    /// <summary>檢查約束定義（Check 用）</summary>
    public string? CheckDefinition { get; init; }

    /// <summary>預設值定義（Default 用）</summary>
    public string? DefaultDefinition { get; init; }

    /// <summary>預設約束套用的欄位（Default 用）</summary>
    public string? DefaultColumn { get; init; }
}

/// <summary>
/// 約束類型
/// </summary>
public enum ConstraintType
{
    PrimaryKey,
    ForeignKey,
    Unique,
    Check,
    Default
}
```

#### SchemaIndex.cs

```csharp
namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 索引資訊
/// </summary>
public class SchemaIndex
{
    /// <summary>索引名稱</summary>
    public string IndexName { get; init; } = string.Empty;

    /// <summary>索引類型</summary>
    public IndexType IndexType { get; init; }

    /// <summary>是否唯一</summary>
    public bool IsUnique { get; init; }

    /// <summary>是否為主鍵索引</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>鍵值欄位（按順序）</summary>
    public IReadOnlyList<IndexColumn> KeyColumns { get; init; } = [];

    /// <summary>Include 欄位</summary>
    public IReadOnlyList<string> IncludeColumns { get; init; } = [];

    /// <summary>Filter 條件</summary>
    public string? FilterDefinition { get; init; }

    /// <summary>Fill Factor</summary>
    public int? FillFactor { get; init; }
}

/// <summary>
/// 索引類型
/// </summary>
public enum IndexType
{
    Clustered,
    NonClustered,
    Heap
}

/// <summary>
/// 索引欄位
/// </summary>
public class IndexColumn
{
    /// <summary>欄位名稱</summary>
    public string ColumnName { get; init; } = string.Empty;

    /// <summary>是否降序</summary>
    public bool IsDescending { get; init; }

    /// <summary>欄位順序</summary>
    public int KeyOrdinal { get; init; }
}
```

#### SchemaDifference.cs

```csharp
namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 單一差異項目
/// </summary>
public class SchemaDifference
{
    /// <summary>差異類型</summary>
    public DifferenceType DifferenceType { get; init; }

    /// <summary>差異類別</summary>
    public DifferenceCategory Category { get; init; }

    /// <summary>物件完整名稱（如 dbo.Users）</summary>
    public string ObjectName { get; init; } = string.Empty;

    /// <summary>屬性名稱（如 DataType, MaxLength）</summary>
    public string? PropertyName { get; init; }

    /// <summary>子物件名稱（如欄位名稱）</summary>
    public string? SubObjectName { get; init; }

    /// <summary>各資料庫的值</summary>
    public IReadOnlyDictionary<Guid, object?> Values { get; init; } =
        new Dictionary<Guid, object?>();

    /// <summary>建議的標準值（根據最大化原則）</summary>
    public object? SuggestedValue { get; init; }

    /// <summary>是否有衝突需要人工決定</summary>
    public bool HasConflict { get; init; }

    /// <summary>關聯的衝突（如果有）</summary>
    public SchemaConflict? Conflict { get; init; }

    /// <summary>差異描述</summary>
    public string Description { get; init; } = string.Empty;
}
```

#### SchemaConflict.cs

```csharp
namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 需要人工決定的衝突
/// </summary>
public class SchemaConflict
{
    /// <summary>衝突 ID</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>衝突類型</summary>
    public ConflictType ConflictType { get; init; }

    /// <summary>嚴重程度</summary>
    public ConflictSeverity Severity { get; init; }

    /// <summary>物件完整名稱</summary>
    public string ObjectName { get; init; } = string.Empty;

    /// <summary>屬性名稱</summary>
    public string PropertyName { get; init; } = string.Empty;

    /// <summary>子物件名稱</summary>
    public string? SubObjectName { get; init; }

    /// <summary>衝突描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>各資料庫的值</summary>
    public IReadOnlyDictionary<Guid, object?> Values { get; init; } =
        new Dictionary<Guid, object?>();

    /// <summary>可選的解決方案</summary>
    public IReadOnlyList<ConflictOption> Options { get; init; } = [];

    /// <summary>已選擇的解決方案（人工決定後填入）</summary>
    public ConflictResolution? Resolution { get; set; }

    /// <summary>是否已解決</summary>
    public bool IsResolved => Resolution != null;
}

/// <summary>
/// 衝突選項
/// </summary>
public class ConflictOption
{
    /// <summary>選項來源（連線 ID）</summary>
    public Guid? SourceConnectionId { get; init; }

    /// <summary>選項值</summary>
    public object? Value { get; init; }

    /// <summary>選項描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>是否為自訂值</summary>
    public bool IsCustom { get; init; }
}

/// <summary>
/// 衝突解決方案
/// </summary>
public class ConflictResolution
{
    /// <summary>選擇的選項</summary>
    public ConflictOption SelectedOption { get; init; } = null!;

    /// <summary>解決時間</summary>
    public DateTime ResolvedAt { get; init; }

    /// <summary>備註</summary>
    public string? Notes { get; init; }
}
```

#### 列舉定義

```csharp
// DifferenceType.cs
namespace TableSpec.Domain.Enums;

/// <summary>
/// 差異類型
/// </summary>
public enum DifferenceType
{
    /// <summary>物件存在於某些資料庫，但不存在於其他</summary>
    Missing,

    /// <summary>物件存在於所有資料庫，但屬性不同</summary>
    Modified,

    /// <summary>物件只存在於單一資料庫</summary>
    Extra
}

// DifferenceCategory.cs
namespace TableSpec.Domain.Enums;

/// <summary>
/// 差異類別
/// </summary>
public enum DifferenceCategory
{
    Table,
    Column,
    PrimaryKey,
    ForeignKey,
    UniqueConstraint,
    CheckConstraint,
    DefaultConstraint,
    Index,
    View,
    StoredProcedure,
    Function,
    Trigger
}

// ConflictType.cs
namespace TableSpec.Domain.Enums;

/// <summary>
/// 衝突類型
/// </summary>
public enum ConflictType
{
    /// <summary>資料型別不相容</summary>
    IncompatibleDataType,

    /// <summary>預設值不同</summary>
    DifferentDefault,

    /// <summary>Nullable 設定不同</summary>
    DifferentNullability,

    /// <summary>Identity 設定不同</summary>
    DifferentIdentity,

    /// <summary>外鍵規則不同</summary>
    DifferentForeignKeyRule,

    /// <summary>物件定義不同（SP/Function/View）</summary>
    DifferentDefinition,

    /// <summary>Collation 不同</summary>
    DifferentCollation
}

// ConflictSeverity.cs
namespace TableSpec.Domain.Enums;

/// <summary>
/// 衝突嚴重程度
/// </summary>
public enum ConflictSeverity
{
    /// <summary>低 - 不影響功能</summary>
    Low,

    /// <summary>中 - 可能影響功能</summary>
    Medium,

    /// <summary>高 - 需要特別注意</summary>
    High
}
```

#### SchemaComparison.cs

```csharp
namespace TableSpec.Domain.Entities.SchemaCompare;

/// <summary>
/// 完整比較結果
/// </summary>
public class SchemaComparison
{
    /// <summary>比較 ID</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>比較時間</summary>
    public DateTime ComparedAt { get; init; }

    /// <summary>參與比較的 Schema 快照</summary>
    public IReadOnlyList<DatabaseSchema> Schemas { get; init; } = [];

    /// <summary>所有差異</summary>
    public IReadOnlyList<SchemaDifference> Differences { get; init; } = [];

    /// <summary>所有衝突</summary>
    public IReadOnlyList<SchemaConflict> Conflicts { get; init; } = [];

    /// <summary>統計摘要</summary>
    public ComparisonSummary Summary { get; init; } = new();
}

/// <summary>
/// 比較統計摘要
/// </summary>
public class ComparisonSummary
{
    /// <summary>資料庫數量</summary>
    public int DatabaseCount { get; init; }

    /// <summary>表格差異數</summary>
    public int TableDifferenceCount { get; init; }

    /// <summary>欄位差異數</summary>
    public int ColumnDifferenceCount { get; init; }

    /// <summary>索引差異數</summary>
    public int IndexDifferenceCount { get; init; }

    /// <summary>約束差異數</summary>
    public int ConstraintDifferenceCount { get; init; }

    /// <summary>View 差異數</summary>
    public int ViewDifferenceCount { get; init; }

    /// <summary>SP 差異數</summary>
    public int ProcedureDifferenceCount { get; init; }

    /// <summary>Function 差異數</summary>
    public int FunctionDifferenceCount { get; init; }

    /// <summary>Trigger 差異數</summary>
    public int TriggerDifferenceCount { get; init; }

    /// <summary>總衝突數</summary>
    public int TotalConflictCount { get; init; }

    /// <summary>已解決衝突數</summary>
    public int ResolvedConflictCount { get; init; }

    /// <summary>高嚴重度衝突數</summary>
    public int HighSeverityConflictCount { get; init; }
}
```

### 3.3 介面定義

#### ISchemaCollector.cs

```csharp
namespace TableSpec.Domain.Interfaces;

/// <summary>
/// Schema 收集器介面
/// </summary>
public interface ISchemaCollector
{
    /// <summary>
    /// 收集指定資料庫的完整 Schema
    /// </summary>
    /// <param name="connectionString">連線字串</param>
    /// <param name="connectionId">連線設定檔 ID</param>
    /// <param name="connectionName">連線名稱</param>
    /// <param name="progress">進度回報</param>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>完整的 Schema 快照</returns>
    Task<DatabaseSchema> CollectAsync(
        string connectionString,
        Guid connectionId,
        string connectionName,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
```

---

## 四、Application 層設計

### 4.1 新增檔案清單

```
src/TableSpec.Application/
└── Services/
    ├── ISchemaCompareService.cs
    ├── SchemaCompareService.cs
    ├── ISchemaReportService.cs
    └── SchemaReportService.cs
```

### 4.2 服務介面設計

#### ISchemaCompareService.cs

```csharp
namespace TableSpec.Application.Services;

/// <summary>
/// Schema 比較服務
/// </summary>
public interface ISchemaCompareService
{
    /// <summary>
    /// 從多個連線收集 Schema
    /// </summary>
    Task<IReadOnlyList<DatabaseSchema>> CollectSchemasAsync(
        IEnumerable<ConnectionProfile> connections,
        IProgress<SchemaCollectionProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 比較多個 Schema
    /// </summary>
    Task<SchemaComparison> CompareAsync(
        IReadOnlyList<DatabaseSchema> schemas,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 解決衝突
    /// </summary>
    void ResolveConflict(SchemaConflict conflict, ConflictOption option, string? notes = null);

    /// <summary>
    /// 取得建議的標準結構
    /// </summary>
    Task<DatabaseSchema> GenerateStandardSchemaAsync(
        SchemaComparison comparison,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Schema 收集進度
/// </summary>
public class SchemaCollectionProgress
{
    public int TotalConnections { get; init; }
    public int CompletedConnections { get; init; }
    public string CurrentConnectionName { get; init; } = string.Empty;
    public string CurrentStep { get; init; } = string.Empty;
}
```

#### ISchemaReportService.cs

```csharp
namespace TableSpec.Application.Services;

/// <summary>
/// Schema 報告服務
/// </summary>
public interface ISchemaReportService
{
    /// <summary>
    /// 匯出比較結果到 Excel
    /// </summary>
    Task<byte[]> ExportToExcelAsync(
        SchemaComparison comparison,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 產生文字摘要
    /// </summary>
    string GenerateSummary(SchemaComparison comparison);
}
```

### 4.3 服務實作重點

**SchemaCompareService 比較邏輯：**

```
1. 建立物件索引
   - 所有 Schema 的 Tables 建立 Dictionary<FullName, SchemaTable>
   - 同理處理 Views, Procedures, Functions, Triggers

2. 比較 Tables
   FOR EACH unique table name:
     - 檢查存在性（哪些 DB 有/沒有）
     - 如果存在於多個 DB，比較欄位
     - 比較約束（PK, FK, Unique, Check, Default）
     - 比較索引

3. 比較 Columns
   FOR EACH unique column name in table:
     - 檢查存在性
     - 比較屬性：DataType, MaxLength, Nullable, Default, Identity, Collation
     - 根據最大化原則決定建議值
     - 如果型別不相容，標記為衝突

4. 應用最大化原則
   - varchar/nvarchar: 取 MAX(MaxLength)
   - decimal: 取 MAX(Precision), MAX(Scale)
   - Nullable: 如果任一為 true，建議 true
   - 型別不同: 標記衝突（需人工）

5. 識別衝突
   - 型別不相容（如 varchar vs int）
   - Default 值不同
   - Identity 設定不同
   - FK ON DELETE/UPDATE 規則不同
   - SP/Function 定義不同
```

---

## 五、Infrastructure 層設計

### 5.1 新增檔案清單

```
src/TableSpec.Infrastructure/
├── Services/
│   ├── MssqlSchemaCollector.cs
│   └── SchemaCompareExcelExporter.cs
└── Sql/
    └── SchemaCollectorQueries.cs
```

### 5.2 MssqlSchemaCollector 實作

收集 Schema 的 SQL 查詢（整合到 `SchemaCollectorQueries.cs`）：

```csharp
namespace TableSpec.Infrastructure.Sql;

/// <summary>
/// Schema 收集器 SQL 查詢
/// </summary>
internal static class SchemaCollectorQueries
{
    /// <summary>取得資料庫基本資訊</summary>
    public const string GetDatabaseInfo = @"
        SELECT
            DB_NAME() AS DatabaseName,
            @@SERVERNAME AS ServerName,
            @@VERSION AS SqlServerVersion,
            DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS Collation";

    /// <summary>取得所有表格</summary>
    public const string GetTables = @"
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            ep.value AS Description
        FROM sys.tables t
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        LEFT JOIN sys.extended_properties ep
            ON ep.major_id = t.object_id
            AND ep.minor_id = 0
            AND ep.name = 'MS_Description'
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name";

    /// <summary>取得所有欄位</summary>
    public const string GetColumns = @"
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            c.name AS ColumnName,
            c.column_id AS OrdinalPosition,
            TYPE_NAME(c.user_type_id) AS DataType,
            c.max_length AS MaxLength,
            c.precision AS NumericPrecision,
            c.scale AS NumericScale,
            c.is_nullable AS IsNullable,
            dc.definition AS DefaultValue,
            c.is_identity AS IsIdentity,
            IDENT_SEED(s.name + '.' + t.name) AS IdentitySeed,
            IDENT_INCR(s.name + '.' + t.name) AS IdentityIncrement,
            c.is_computed AS IsComputed,
            cc.definition AS ComputedDefinition,
            c.collation_name AS Collation,
            ep.value AS Description
        FROM sys.tables t
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        INNER JOIN sys.columns c ON t.object_id = c.object_id
        LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
        LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id
        LEFT JOIN sys.extended_properties ep
            ON ep.major_id = c.object_id
            AND ep.minor_id = c.column_id
            AND ep.name = 'MS_Description'
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, c.column_id";

    /// <summary>取得主鍵</summary>
    public const string GetPrimaryKeys = @"
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            kc.name AS ConstraintName,
            c.name AS ColumnName,
            ic.key_ordinal AS KeyOrdinal
        FROM sys.key_constraints kc
        INNER JOIN sys.tables t ON kc.parent_object_id = t.object_id
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        INNER JOIN sys.index_columns ic ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE kc.type = 'PK'
        ORDER BY s.name, t.name, ic.key_ordinal";

    /// <summary>取得外鍵</summary>
    public const string GetForeignKeys = @"
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            fk.name AS ConstraintName,
            COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
            OBJECT_SCHEMA_NAME(fkc.referenced_object_id) + '.' + OBJECT_NAME(fkc.referenced_object_id) AS ReferencedTable,
            COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ReferencedColumn,
            fk.delete_referential_action_desc AS OnDeleteAction,
            fk.update_referential_action_desc AS OnUpdateAction,
            fkc.constraint_column_id AS KeyOrdinal
        FROM sys.foreign_keys fk
        INNER JOIN sys.tables t ON fk.parent_object_id = t.object_id
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
        ORDER BY s.name, t.name, fk.name, fkc.constraint_column_id";

    /// <summary>取得唯一約束</summary>
    public const string GetUniqueConstraints = @"
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            kc.name AS ConstraintName,
            c.name AS ColumnName,
            ic.key_ordinal AS KeyOrdinal
        FROM sys.key_constraints kc
        INNER JOIN sys.tables t ON kc.parent_object_id = t.object_id
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        INNER JOIN sys.index_columns ic ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE kc.type = 'UQ'
        ORDER BY s.name, t.name, kc.name, ic.key_ordinal";

    /// <summary>取得檢查約束</summary>
    public const string GetCheckConstraints = @"
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            cc.name AS ConstraintName,
            cc.definition AS CheckDefinition
        FROM sys.check_constraints cc
        INNER JOIN sys.tables t ON cc.parent_object_id = t.object_id
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        ORDER BY s.name, t.name, cc.name";

    /// <summary>取得預設約束</summary>
    public const string GetDefaultConstraints = @"
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            dc.name AS ConstraintName,
            c.name AS ColumnName,
            dc.definition AS DefaultDefinition
        FROM sys.default_constraints dc
        INNER JOIN sys.tables t ON dc.parent_object_id = t.object_id
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
        ORDER BY s.name, t.name, dc.name";

    /// <summary>取得索引</summary>
    public const string GetIndexes = @"
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            i.name AS IndexName,
            i.type_desc AS IndexType,
            i.is_unique AS IsUnique,
            i.is_primary_key AS IsPrimaryKey,
            c.name AS ColumnName,
            ic.key_ordinal AS KeyOrdinal,
            ic.is_descending_key AS IsDescending,
            ic.is_included_column AS IsIncluded,
            i.filter_definition AS FilterDefinition,
            i.fill_factor AS FillFactor
        FROM sys.indexes i
        INNER JOIN sys.tables t ON i.object_id = t.object_id
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE i.type > 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
        ORDER BY s.name, t.name, i.name, ic.key_ordinal";

    /// <summary>取得 Views</summary>
    public const string GetViews = @"
        SELECT
            s.name AS SchemaName,
            v.name AS ViewName,
            m.definition AS Definition,
            ep.value AS Description
        FROM sys.views v
        INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
        LEFT JOIN sys.sql_modules m ON v.object_id = m.object_id
        LEFT JOIN sys.extended_properties ep
            ON ep.major_id = v.object_id
            AND ep.minor_id = 0
            AND ep.name = 'MS_Description'
        WHERE v.is_ms_shipped = 0
        ORDER BY s.name, v.name";

    /// <summary>取得 Stored Procedures</summary>
    public const string GetProcedures = @"
        SELECT
            s.name AS SchemaName,
            p.name AS ProcedureName,
            m.definition AS Definition,
            ep.value AS Description
        FROM sys.procedures p
        INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
        LEFT JOIN sys.sql_modules m ON p.object_id = m.object_id
        LEFT JOIN sys.extended_properties ep
            ON ep.major_id = p.object_id
            AND ep.minor_id = 0
            AND ep.name = 'MS_Description'
        WHERE p.is_ms_shipped = 0
        ORDER BY s.name, p.name";

    /// <summary>取得 Functions</summary>
    public const string GetFunctions = @"
        SELECT
            s.name AS SchemaName,
            o.name AS FunctionName,
            o.type_desc AS FunctionType,
            m.definition AS Definition,
            ep.value AS Description
        FROM sys.objects o
        INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
        LEFT JOIN sys.sql_modules m ON o.object_id = m.object_id
        LEFT JOIN sys.extended_properties ep
            ON ep.major_id = o.object_id
            AND ep.minor_id = 0
            AND ep.name = 'MS_Description'
        WHERE o.type IN ('FN', 'IF', 'TF', 'AF')
        ORDER BY s.name, o.name";

    /// <summary>取得 Triggers</summary>
    public const string GetTriggers = @"
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            tr.name AS TriggerName,
            tr.is_disabled AS IsDisabled,
            m.definition AS Definition,
            ep.value AS Description
        FROM sys.triggers tr
        INNER JOIN sys.tables t ON tr.parent_id = t.object_id
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        LEFT JOIN sys.sql_modules m ON tr.object_id = m.object_id
        LEFT JOIN sys.extended_properties ep
            ON ep.major_id = tr.object_id
            AND ep.minor_id = 0
            AND ep.name = 'MS_Description'
        WHERE tr.is_ms_shipped = 0
        ORDER BY s.name, t.name, tr.name";
}
```

---

## 六、Desktop 層設計

> **重要**：本專案已採用 MDI (多文件介面) 架構，Schema Compare 將作為 Document 類型整合，而非獨立視窗。

### 6.1 現有 MDI 架構

```
MainWindow
├── 左側：ObjectTree（物件樹）
└── 右側：TabControl (Documents)
    ├── TableDetailDocumentView    → TableDetailDocumentViewModel
    ├── SqlQueryDocumentView       → SqlQueryDocumentViewModel
    ├── ColumnSearchDocumentView   → ColumnSearchDocumentViewModel
    └── [新增] SchemaCompareDocumentView → SchemaCompareDocumentViewModel
```

**DocumentViewModel 基類特性：**
- `Title` - 分頁標題
- `Icon` - 分頁圖示
- `CanClose` - 是否可關閉
- `DocumentType` - 文件類型識別碼
- `DocumentKey` - 唯一識別碼（防止重複開啟同一比較）
- `CloseRequested` 事件

### 6.2 新增檔案清單

```
src/TableSpec.Desktop/
├── ViewModels/
│   ├── SchemaCompareDocumentViewModel.cs    # 主 ViewModel（繼承 DocumentViewModel）
│   └── Models/
│       ├── ConnectionSelectionItem.cs       # 連線選擇項目（含勾選狀態）
│       ├── SchemaCompareTreeNode.cs         # 差異樹節點
│       ├── DifferenceMatrixRow.cs           # 差異矩陣表格行
│       ├── ResolutionOption.cs              # 解決方案選項
│       ├── ValidationResult.cs              # 驗證結果項目
│       └── MigrationPlan.cs                 # Migration 執行計畫
├── Views/
│   ├── SchemaCompareDocumentView.axaml      # 主 View（三模式切換）
│   ├── SchemaCompareDocumentView.axaml.cs
│   ├── Controls/
│   │   ├── DifferenceMatrixControl.axaml    # 差異矩陣表格控件
│   │   ├── ResolutionPanelControl.axaml     # 解決方案面板控件
│   │   ├── ValidationResultControl.axaml   # 驗證結果顯示控件
│   │   └── MigrationPlanControl.axaml       # Migration 計畫控件
│   └── Dialogs/
│       ├── BackupConfirmDialog.axaml        # 備份確認對話框
│       └── ExecutionConfirmDialog.axaml     # 執行確認對話框
└── Converters/
    ├── RiskLevelColorConverter.cs           # 風險等級顏色轉換
    ├── RiskLevelIconConverter.cs            # 風險等級圖示轉換
    ├── DifferenceTypeIconConverter.cs       # 差異類型圖示轉換
    └── ValidationStatusIconConverter.cs     # 驗證狀態圖示轉換
```

```
src/TableSpec.Application/
└── Services/
    ├── IMigrationExecutor.cs                # Migration 執行器介面
    ├── MigrationExecutor.cs                 # Migration 執行器實作
    ├── IPreflightValidator.cs               # 執行前驗證器介面
    ├── PreflightValidator.cs                # 執行前驗證器實作
    ├── IPostExecutionValidator.cs           # 執行後驗證器介面
    ├── PostExecutionValidator.cs            # 執行後驗證器實作
    └── IRiskAssessor.cs                     # 風險評估器介面
    └── RiskAssessor.cs                      # 風險評估器實作
```

```
src/TableSpec.Domain/
├── Entities/SchemaCompare/
│   ├── ... (既有的 Entity)
│   ├── MigrationScript.cs                   # Migration 腳本
│   ├── RollbackScript.cs                    # Rollback 腳本
│   ├── ValidationCheckResult.cs             # 驗證檢查結果
│   └── RiskAssessment.cs                    # 風險評估結果
└── Enums/
    ├── ... (既有的 Enum)
    ├── RiskLevel.cs                         # 風險等級
    ├── ValidationStatus.cs                  # 驗證狀態
    └── MigrationPhase.cs                    # Migration 階段
```

### 6.3 UI 設計（MDI Document）

UI 分為三個主要模式/分頁：比較模式、解決模式、執行模式

#### 6.3.1 比較模式 - 差異呈現

採用「樹狀圖導航 + 矩陣表格詳情」的三欄式佈局：

```
┌───────────────────────────────────────────────────────────────────────────┐
│ [工具列]                                                                   │
│  [全選] [取消全選] [開始比較] [匯出 Excel] [匯出 SQL]  [切換模式 ▼]        │
├────────────┬────────────────────────────────────────────────────┬─────────┤
│ [差異導航]  │ [差異矩陣表格]                                      │ [摘要]  │
│            │                                                    │         │
│ ┌─ 🟢 低 3 │ ┌────────────────────────────────────────────────┐│ 總差異  │
│ │  └─新增欄│ │ 屬性     │ 客戶A   │ 客戶B   │ 客戶C   │ 建議值 ││ 28 項   │
│ ├─ 🟡 中 5 │ ├──────────┼─────────┼─────────┼─────────┼────────┤│         │
│ │  └─修改NA│ │ DataType │ varchar │ nvarchar│ varchar │   ?    ││ 🟢 低 8 │
│ ├─ 🔴 高 2 │ │ Length   │ 100     │ 100     │ 50      │ 100    ││ 🟡 中 15│
│ │  └─縮短長│ │ Nullable │ YES     │ YES     │ NO      │ YES    ││ 🔴 高 5 │
│ └─ ⛔ 禁 0 │ │ Default  │ NULL    │ ''      │ NULL    │   ?    ││         │
│            │ └────────────────────────────────────────────────┘│ 已解決  │
│ ─────────  │                                                    │ 12/28   │
│ Tables (5) │ [值比較視覺化]                                       │         │
│ ├─ dbo.Use │  客戶A ████████████████████  varchar(100)          │ 風險分數│
│ │  ├─ Colu │  客戶B ████████████████████  nvarchar(100) ⚠不同   │ 85 分   │
│ │  │  ├─🔴 │  客戶C ██████████            varchar(50)   ⚠短    │         │
│ │  │  ├─🟡 │                                                    │ [執行]  │
│ │  │  └─🟢 │ ────────────────────────────────────────────────── │ 需先解決│
│ │  └─ Inde │ [解決方案選擇]                                       │ 16 項   │
│ └─ dbo.Ord │  ○ 採用 客戶A 的值: varchar(100)                    │         │
│            │  ○ 採用 客戶B 的值: nvarchar(100) ← 建議            │         │
│ Views (2)  │  ○ 採用 客戶C 的值: varchar(50)                     │         │
│ SPs (10)   │  ○ 自訂值: [________________] [確認]                │         │
│ Functions 3│                                                    │         │
├────────────┴────────────────────────────────────────────────────┴─────────┤
│ [狀態列] 選擇 dbo.Users.Email | 風險: 🔴 高 | 類型衝突需人工決定           │
└───────────────────────────────────────────────────────────────────────────┘
```

#### 6.3.2 解決模式 - 批次處理

專門用於處理待解決的差異項目：

```
┌───────────────────────────────────────────────────────────────────────────┐
│ [工具列]                                                                   │
│  [全部採用建議] [批次套用相同類型] [重設] [返回比較]     進度: 12/28 (43%) │
├───────────────────────────────────────────────────────────────────────────┤
│ [待解決清單]                                                               │
│ ┌───────────────────────────────────────────────────────────────────────┐ │
│ │ # │ 風險 │ 物件                │ 屬性     │ 問題描述        │ 解決狀態 │ │
│ ├───┼──────┼─────────────────────┼──────────┼─────────────────┼──────────┤ │
│ │ 1 │ 🔴   │ dbo.Users.Email     │ DataType │ 型別不一致      │ ⏳ 待解決│ │
│ │ 2 │ 🔴   │ dbo.Users.Email     │ Default  │ 預設值不同      │ ⏳ 待解決│ │
│ │ 3 │ 🟡   │ dbo.Users.Phone     │ 存在性   │ 客戶B 缺少此欄位│ ✅ 已解決│ │
│ │ 4 │ 🟡   │ dbo.Orders.Status   │ Nullable │ NULL 設定不同   │ ⏳ 待解決│ │
│ │...│ ...  │ ...                 │ ...      │ ...             │ ...      │ │
│ └───────────────────────────────────────────────────────────────────────┘ │
├───────────────────────────────────────────────────────────────────────────┤
│ [選中項目詳情] #1 dbo.Users.Email - DataType                               │
│ ┌─────────────────────────────────────────────────────────────────────┐   │
│ │ 問題: 欄位資料型別在不同資料庫間不一致                                │   │
│ │                                                                     │   │
│ │ 影響評估:                                                            │   │
│ │ • 如果選擇 nvarchar，varchar 資料庫需要轉換（安全，但增加空間）      │   │
│ │ • 如果選擇 varchar，nvarchar 資料庫可能有 Unicode 資料遺失風險       │   │
│ │ • 受影響資料筆數: 客戶A: 15,234 筆, 客戶B: 8,721 筆, 客戶C: 5,102 筆  │   │
│ │                                                                     │   │
│ │ 解決方案:                                                            │   │
│ │ ┌─────────────────────────────────────────────────────────────────┐ │   │
│ │ │ ○ 採用 nvarchar(100)  [推薦 - 相容性最佳]                        │ │   │
│ │ │   風險: 🟡 中 | 空間增加約 2x | 需要 ALTER COLUMN               │ │   │
│ │ │                                                                 │ │   │
│ │ │ ○ 採用 varchar(100)                                             │ │   │
│ │ │   風險: 🔴 高 | 可能有 Unicode 資料遺失 | 需先檢查資料           │ │   │
│ │ │                                                                 │ │   │
│ │ │ ○ 自訂: [________________] 例如: nvarchar(200)                  │ │   │
│ │ └─────────────────────────────────────────────────────────────────┘ │   │
│ │                                                                     │   │
│ │ [套用此選擇] [套用到所有相同類型的衝突] [跳過]                        │   │
│ └─────────────────────────────────────────────────────────────────────┘   │
├───────────────────────────────────────────────────────────────────────────┤
│ [狀態列] 待解決: 16 項 | 已解決: 12 項 | 總風險分數: 85                    │
└───────────────────────────────────────────────────────────────────────────┘
```

#### 6.3.3 執行模式 - Migration 執行

執行前驗證和 Migration 執行：

```
┌───────────────────────────────────────────────────────────────────────────┐
│ [工具列]                                                                   │
│  [執行前檢查] [產生 SQL 腳本] [執行 Migration] [返回比較]                   │
├───────────────────────────────────────────────────────────────────────────┤
│ [執行前驗證結果]                                                           │
│ ┌───────────────────────────────────────────────────────────────────────┐ │
│ │ 檢查項目          │ 狀態 │ 詳情                                       │ │
│ ├───────────────────┼──────┼────────────────────────────────────────────┤ │
│ │ 連線狀態          │ ✅   │ 3/3 資料庫連線正常                         │ │
│ │ 權限驗證          │ ✅   │ 所有資料庫皆有 ALTER 權限                   │ │
│ │ 磁碟空間          │ ✅   │ 客戶A: 50GB, 客戶B: 30GB, 客戶C: 45GB      │ │
│ │ 備份狀態          │ ⚠️   │ 客戶B 最後備份: 2 天前 (需要更新備份)       │ │
│ │ 相依性分析        │ ✅   │ 已分析 FK/View/SP 相依關係                  │ │
│ │ 資料影響評估      │ ✅   │ 共影響 29,057 筆資料                        │ │
│ │ 風險評分          │ 🟡   │ 總分: 85 (中風險)                          │ │
│ │ 測試環境驗證      │ ❌   │ 包含高風險變更，需先在測試環境執行          │ │
│ └───────────────────────────────────────────────────────────────────────┘ │
├───────────────────────────────────────────────────────────────────────────┤
│ [執行計畫預覽]                                                             │
│ ┌───────────────────────────────────────────────────────────────────────┐ │
│ │ Phase 1: 移除相依物件 (預估 5 秒)                                      │ │
│ │   • DROP INDEX IX_Email ON dbo.Users                                  │ │
│ │   • ALTER TABLE dbo.Orders DROP CONSTRAINT FK_Orders_Users            │ │
│ │                                                                       │ │
│ │ Phase 2: 結構變更 (預估 30 秒)                                         │ │
│ │   • ALTER TABLE dbo.Users ALTER COLUMN Email NVARCHAR(100)            │ │
│ │   • ALTER TABLE dbo.Users ADD Phone NVARCHAR(20) NULL                 │ │
│ │                                                                       │ │
│ │ Phase 3: 重建相依物件 (預估 10 秒)                                     │ │
│ │   • CREATE INDEX IX_Email ON dbo.Users(Email)                         │ │
│ │   • ALTER TABLE dbo.Orders ADD CONSTRAINT FK_Orders_Users ...         │ │
│ │                                                                       │ │
│ │ [展開完整 SQL] [複製到剪貼簿] [儲存為檔案]                              │ │
│ └───────────────────────────────────────────────────────────────────────┘ │
├───────────────────────────────────────────────────────────────────────────┤
│ [執行選項]                                                                 │
│ ┌───────────────────────────────────────────────────────────────────────┐ │
│ │ 目標資料庫:                                                            │ │
│ │   ☑ 客戶A資料庫 (正式)                                                │ │
│ │   ☑ 客戶B資料庫 (正式)  ⚠️ 備份過期                                    │ │
│ │   ☑ 客戶C資料庫 (正式)                                                │ │
│ │   ☐ 測試環境 (必須先執行)  ← 包含高風險，強制勾選                      │ │
│ │                                                                       │ │
│ │ 執行模式:                                                              │ │
│ │   ○ 僅產生腳本（不執行）                                               │ │
│ │   ○ 逐一確認執行（每個步驟需確認）                                     │ │
│ │   ○ 自動執行（出錯時停止）                                             │ │
│ │                                                                       │ │
│ │ [☑] 執行前自動備份  [☑] 產生 Rollback 腳本  [☑] 執行後驗證            │ │
│ │                                                                       │ │
│ │ [開始執行] [取消]                                                      │ │
│ │                                                                       │ │
│ │ ⚠️ 警告: 包含 2 個高風險變更，建議先在測試環境驗證                      │ │
│ └───────────────────────────────────────────────────────────────────────┘ │
├───────────────────────────────────────────────────────────────────────────┤
│ [狀態列] 準備就緒 | 待執行: 28 個變更 | 預估時間: 45 秒                     │
└───────────────────────────────────────────────────────────────────────────┘
```

#### 6.3.4 視覺化標記規範

| 元素 | 低風險 🟢 | 中風險 🟡 | 高風險 🔴 | 禁止 ⛔ |
|------|---------|---------|---------|--------|
| 背景色 | #E8F5E9 | #FFF8E1 | #FFEBEE | #F5F5F5 |
| 邊框色 | #4CAF50 | #FFC107 | #F44336 | #9E9E9E |
| 圖示 | ✓ / + | ⚠ | ✗ / ! | 🚫 |

#### 6.3.5 差異類型圖示

| 差異類型 | 圖示 | 說明 |
|---------|------|------|
| 新增 | ➕ | 物件/欄位需要新增 |
| 遺漏 | ➖ | 物件/欄位在某些資料庫缺少 |
| 修改 | ✏️ | 屬性值不一致 |
| 衝突 | ⚠️ | 需要人工決定 |
| 已解決 | ✅ | 已選擇解決方案 |

### 6.4 AXAML 範本

#### SchemaCompareDocumentView.axaml

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:TableSpec.Desktop.ViewModels"
             xmlns:domain="using:TableSpec.Domain.Entities"
             x:Class="TableSpec.Desktop.Views.SchemaCompareDocumentView"
             x:DataType="vm:SchemaCompareDocumentViewModel">

    <Design.DataContext>
        <vm:SchemaCompareDocumentViewModel/>
    </Design.DataContext>

    <Grid RowDefinitions="Auto,*,Auto,Auto">
        <!-- 工具列 -->
        <Border Grid.Row="0" Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}" Padding="10,8">
            <StackPanel Orientation="Horizontal" Spacing="10">
                <Button Content="全選" Command="{Binding SelectAllCommand}"/>
                <Button Content="取消全選" Command="{Binding DeselectAllCommand}"/>
                <Separator/>
                <Button Content="開始比較" Command="{Binding CompareCommand}"
                        IsEnabled="{Binding CanCompare}"/>
                <Button Content="匯出 Excel" Command="{Binding ExportToExcelCommand}"
                        IsEnabled="{Binding HasComparison}"/>
                <ProgressBar IsIndeterminate="True" Width="100"
                             IsVisible="{Binding IsComparing}"/>
            </StackPanel>
        </Border>

        <!-- 主內容區 -->
        <Grid Grid.Row="1" ColumnDefinitions="250,5,*" Margin="10,5">
            <!-- 左側：連線選擇 + 統計 -->
            <Grid Grid.Column="0" RowDefinitions="*,Auto">
                <!-- 連線清單 -->
                <ListBox Grid.Row="0" ItemsSource="{Binding Connections}">
                    <ListBox.ItemTemplate>
                        <DataTemplate x:DataType="vm:ConnectionSelectionItem">
                            <CheckBox IsChecked="{Binding IsSelected}"
                                      Content="{Binding Profile.Name}"/>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <!-- 統計摘要 -->
                <Border Grid.Row="1" Padding="10" Background="{DynamicResource SystemControlBackgroundAltMediumLowBrush}">
                    <StackPanel Spacing="5">
                        <TextBlock Text="{Binding SelectedConnectionCount, StringFormat='已選: {0} 個連線'}"/>
                        <TextBlock Text="{Binding TotalDifferenceCount, StringFormat='差異: {0} 項'}"/>
                        <TextBlock Text="{Binding ConflictSummary}" Foreground="Orange"/>
                    </StackPanel>
                </Border>
            </Grid>

            <GridSplitter Grid.Column="1" ResizeDirection="Columns"/>

            <!-- 右側：差異樹 + 詳情 -->
            <Grid Grid.Column="2" RowDefinitions="*,5,200">
                <!-- 差異樹 -->
                <TreeView Grid.Row="0" ItemsSource="{Binding DifferenceTree}"
                          SelectedItem="{Binding SelectedDifference}">
                    <!-- TreeView DataTemplates -->
                </TreeView>

                <GridSplitter Grid.Row="1" ResizeDirection="Rows"/>

                <!-- 差異詳情 -->
                <Border Grid.Row="2" Padding="10">
                    <ContentControl Content="{Binding SelectedDifference}">
                        <!-- 詳情模板 -->
                    </ContentControl>
                </Border>
            </Grid>
        </Grid>

        <!-- 衝突解決區 -->
        <Border Grid.Row="2" Padding="10" IsVisible="{Binding HasConflicts}">
            <ItemsControl ItemsSource="{Binding UnresolvedConflicts}">
                <!-- 衝突項目模板 -->
            </ItemsControl>
        </Border>

        <!-- 狀態列 -->
        <Border Grid.Row="3" Background="{DynamicResource SystemControlBackgroundChromeMediumBrush}" Padding="10,5">
            <TextBlock Text="{Binding StatusMessage}"/>
        </Border>
    </Grid>
</UserControl>
```

### 6.5 SchemaCompareDocumentViewModel 設計

```csharp
/// <summary>
/// Schema 比較文件 ViewModel（MDI Document）
/// </summary>
public partial class SchemaCompareDocumentViewModel : DocumentViewModel
{
    private readonly IConnectionManager _connectionManager;
    private readonly ISchemaCompareService _compareService;
    private readonly ISchemaReportService _reportService;
    private static int _instanceCount;
    private readonly int _instanceId;

    // === DocumentViewModel 覆寫 ===
    public override string DocumentType => "SchemaCompare";
    public override string DocumentKey => $"{DocumentType}:{_instanceId}";

    // === 連線選擇 ===
    public ObservableCollection<ConnectionSelectionItem> Connections { get; } = [];

    public IEnumerable<ConnectionProfile> SelectedConnections =>
        Connections.Where(c => c.IsSelected).Select(c => c.Profile);

    [ObservableProperty]
    private int _selectedConnectionCount;

    // === 比較結果 ===
    [ObservableProperty]
    private SchemaComparison? _comparison;

    [ObservableProperty]
    private ObservableCollection<SchemaCompareTreeNode> _differenceTree = [];

    [ObservableProperty]
    private SchemaCompareTreeNode? _selectedDifference;

    // === 衝突處理 ===
    [ObservableProperty]
    private ObservableCollection<SchemaConflict> _conflicts = [];

    public IEnumerable<SchemaConflict> UnresolvedConflicts =>
        Conflicts.Where(c => !c.IsResolved);

    public bool HasConflicts => Conflicts.Any(c => !c.IsResolved);

    // === 狀態 ===
    [ObservableProperty]
    private bool _isComparing;

    [ObservableProperty]
    private string _statusMessage = "請選擇至少 2 個資料庫連線進行比較";

    [ObservableProperty]
    private int _totalDifferenceCount;

    [ObservableProperty]
    private string _conflictSummary = string.Empty;

    // === 計算屬性 ===
    public bool CanCompare => SelectedConnectionCount >= 2 && !IsComparing;
    public bool HasComparison => Comparison != null;

    // === 建構函式 ===
    public SchemaCompareDocumentViewModel()
    {
        // Design-time constructor
        _instanceId = ++_instanceCount;
        Title = "Schema 比較";
        Icon = "🔀";
        CanClose = true;
    }

    public SchemaCompareDocumentViewModel(
        IConnectionManager connectionManager,
        ISchemaCompareService compareService,
        ISchemaReportService reportService)
    {
        _connectionManager = connectionManager;
        _compareService = compareService;
        _reportService = reportService;
        _instanceId = ++_instanceCount;

        Title = "Schema 比較";
        Icon = "🔀";
        CanClose = true;

        LoadConnectionProfiles();
    }

    private void LoadConnectionProfiles()
    {
        Connections.Clear();
        var profiles = _connectionManager?.GetAllProfiles() ?? [];
        foreach (var profile in profiles)
        {
            var item = new ConnectionSelectionItem(profile);
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ConnectionSelectionItem.IsSelected))
                {
                    UpdateSelectedCount();
                }
            };
            Connections.Add(item);
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedConnectionCount = Connections.Count(c => c.IsSelected);
        OnPropertyChanged(nameof(CanCompare));
    }

    // === 命令 ===
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var conn in Connections)
            conn.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var conn in Connections)
            conn.IsSelected = false;
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (_compareService == null) return;

        try
        {
            IsComparing = true;
            StatusMessage = "正在收集 Schema...";

            // 1. 收集 Schema
            var schemas = await _compareService.CollectSchemasAsync(
                SelectedConnections,
                new Progress<SchemaCollectionProgress>(p =>
                {
                    StatusMessage = $"正在收集: {p.CurrentConnectionName} ({p.CompletedConnections}/{p.TotalConnections})";
                }));

            StatusMessage = "正在比較結構...";

            // 2. 執行比較
            Comparison = await _compareService.CompareAsync(schemas);

            // 3. 更新 UI
            BuildDifferenceTree();
            UpdateConflicts();
            UpdateStatistics();

            StatusMessage = $"比較完成：{TotalDifferenceCount} 個差異，{Conflicts.Count} 個衝突";
        }
        catch (Exception ex)
        {
            StatusMessage = $"錯誤：{ex.Message}";
        }
        finally
        {
            IsComparing = false;
        }
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        if (_reportService == null || Comparison == null) return;

        // 使用 StorageProvider API 儲存檔案
        // ... (類似 MainWindowViewModel 的匯出邏輯)
    }

    [RelayCommand]
    private void ResolveConflict(SchemaConflict conflict, ConflictOption option)
    {
        _compareService?.ResolveConflict(conflict, option);
        OnPropertyChanged(nameof(UnresolvedConflicts));
        OnPropertyChanged(nameof(HasConflicts));
        UpdateConflictSummary();
    }

    private void BuildDifferenceTree() { /* 建立樹狀結構 */ }
    private void UpdateConflicts() { /* 更新衝突清單 */ }
    private void UpdateStatistics() { /* 更新統計數據 */ }
    private void UpdateConflictSummary() { /* 更新衝突摘要 */ }
}
```

### 6.6 輔助類別

#### ConnectionSelectionItem.cs

```csharp
/// <summary>
/// 連線選擇項目（包含勾選狀態）
/// </summary>
public partial class ConnectionSelectionItem : ObservableObject
{
    public ConnectionProfile Profile { get; }

    [ObservableProperty]
    private bool _isSelected;

    public ConnectionSelectionItem(ConnectionProfile profile)
    {
        Profile = profile;
    }
}
```

### 6.7 MainWindow 整合

需要在 `MainWindow.axaml` 的 DataTemplates 中新增映射：

```xml
<TabControl.ContentTemplate>
    <DataTemplate>
        <ContentControl Content="{Binding}">
            <ContentControl.DataTemplates>
                <!-- 現有文件類型 -->
                <DataTemplate DataType="{x:Type vm:TableDetailDocumentViewModel}">
                    <views:TableDetailDocumentView/>
                </DataTemplate>
                <DataTemplate DataType="{x:Type vm:SqlQueryDocumentViewModel}">
                    <views:SqlQueryDocumentView/>
                </DataTemplate>
                <DataTemplate DataType="{x:Type vm:ColumnSearchDocumentViewModel}">
                    <views:ColumnSearchDocumentView/>
                </DataTemplate>
                <!-- 新增 Schema Compare -->
                <DataTemplate DataType="{x:Type vm:SchemaCompareDocumentViewModel}">
                    <views:SchemaCompareDocumentView/>
                </DataTemplate>
            </ContentControl.DataTemplates>
        </ContentControl>
    </DataTemplate>
</TabControl.ContentTemplate>
```

在 `MainWindowViewModel` 中新增開啟命令：

```csharp
[RelayCommand]
private void OpenSchemaCompare()
{
    var doc = App.Services?.GetRequiredService<SchemaCompareDocumentViewModel>()
        ?? new SchemaCompareDocumentViewModel();
    doc.CloseRequested += OnDocumentCloseRequested;
    Documents.Add(doc);
    SelectedDocument = doc;
}
```

在選單中新增入口：

```xml
<MenuItem Header="工具(_T)">
    <!-- 現有項目 -->
    <Separator/>
    <MenuItem Header="Schema 比較(_C)" Command="{Binding OpenSchemaCompareCommand}">
        <MenuItem.Icon>
            <TextBlock Text="🔀" FontSize="14"/>
        </MenuItem.Icon>
    </MenuItem>
</MenuItem>
```

---

## 七、實作步驟

### 階段 1：Domain 層（Day 1-2）

| 步驟 | 工作內容 | 預估 |
|------|---------|------|
| 1.1 | 建立 `Entities/SchemaCompare/` 目錄結構 | 0.5h |
| 1.2 | 實作所有 Entity 類別 | 2h |
| 1.3 | 實作所有 Enum | 0.5h |
| 1.4 | 實作 `ISchemaCollector` 介面 | 0.5h |
| 1.5 | 撰寫 Domain 層單元測試 | 1h |

### 階段 2：Infrastructure 層（Day 3-5）

| 步驟 | 工作內容 | 預估 |
|------|---------|------|
| 2.1 | 實作 `SchemaCollectorQueries.cs` SQL 查詢 | 1h |
| 2.2 | 實作 `MssqlSchemaCollector` | 3h |
| 2.3 | 實作 `SchemaCompareExcelExporter` | 2h |
| 2.4 | 撰寫 Infrastructure 層整合測試 | 2h |

### 階段 3：Application 層（Day 6-8）

| 步驟 | 工作內容 | 預估 |
|------|---------|------|
| 3.1 | 實作 `ISchemaCompareService` 介面 | 0.5h |
| 3.2 | 實作 `SchemaCompareService` 比較邏輯 | 4h |
| 3.3 | 實作最大化原則演算法 | 2h |
| 3.4 | 實作衝突偵測邏輯 | 2h |
| 3.5 | 實作 `ISchemaReportService` | 1h |
| 3.6 | 撰寫 Application 層單元測試 | 2h |

### 階段 4：Desktop 層（Day 9-12）

| 步驟 | 工作內容 | 預估 |
|------|---------|------|
| 4.1 | 建立 `SchemaCompareDocumentView.axaml` UserControl | 2h |
| 4.2 | 實作 `SchemaCompareDocumentViewModel`（繼承 DocumentViewModel） | 3h |
| 4.3 | 建立 `ConnectionSelectionItem` 輔助類別 | 0.5h |
| 4.4 | 實作差異樹狀圖顯示（TreeView + DataTemplates） | 2h |
| 4.5 | 實作衝突解決 UI（ItemsControl + 選項按鈕） | 2h |
| 4.6 | 實作 Excel 匯出功能（StorageProvider API） | 1h |
| 4.7 | 整合到 MainWindow（DataTemplate + 選單 + 命令） | 1h |
| 4.8 | UI 測試與調整 | 2h |

### 階段 5：測試與文件（Day 13-14）

| 步驟 | 工作內容 | 預估 |
|------|---------|------|
| 5.1 | 端對端測試（使用實際資料庫） | 3h |
| 5.2 | 效能測試與優化 | 2h |
| 5.3 | 更新使用者文件 | 1h |
| 5.4 | Code Review 與修正 | 2h |

---

## 八、DI 註冊規劃

在 `Program.cs` 的 `ConfigureServices()` 方法中新增：

```csharp
// === Schema Compare 相關服務 ===

// Infrastructure 層
services.AddSingleton<ISchemaCollector, MssqlSchemaCollector>();

// Application 層
services.AddSingleton<ISchemaCompareService, SchemaCompareService>();
services.AddSingleton<ISchemaReportService, SchemaReportService>();

// Desktop 層 - ViewModel（Transient，每次開啟新分頁都是新實例）
services.AddTransient<SchemaCompareDocumentViewModel>();
```

---

## 九、Excel 報告格式設計

### 9.1 Sheet 結構

| Sheet 名稱 | 內容 |
|-----------|------|
| 摘要 | 比較統計、資料庫清單、差異計數 |
| Tables 差異 | 所有表格差異明細 |
| Columns 差異 | 所有欄位差異明細 |
| Indexes 差異 | 所有索引差異明細 |
| Constraints 差異 | 所有約束差異明細 |
| Views 差異 | 所有 View 差異明細 |
| SPs 差異 | 所有 SP 差異明細 |
| Functions 差異 | 所有 Function 差異明細 |
| Triggers 差異 | 所有 Trigger 差異明細 |
| 衝突清單 | 需人工決定的衝突 |
| 解決方案 | 已解決的衝突記錄 |

### 9.2 Tables 差異 Sheet 範例

| 物件名稱 | 差異類型 | 屬性 | 客戶A | 客戶B | 客戶C | 建議值 | 衝突 |
|---------|---------|------|-------|-------|-------|--------|------|
| dbo.Users | 欄位差異 | Email.DataType | varchar | nvarchar | varchar | ⚠ 衝突 | Y |
| dbo.Users | 欄位差異 | Email.MaxLength | 100 | 100 | 50 | 100 | N |
| dbo.Users | 欄位缺少 | Phone | ✓ | ✗ | ✓ | ✓ | N |
| dbo.Orders | 表格缺少 | - | ✓ | ✗ | ✓ | ✓ | N |

---

## 十、風險與緩解措施

| 風險 | 影響 | 緩解措施 |
|------|------|---------|
| 大量資料庫造成記憶體壓力 | 效能問題 | 分批收集、串流處理 |
| SP/Function 定義比較複雜 | 假陽性差異 | 正規化後比較（移除空白/註解） |
| 連線逾時 | 收集失敗 | 可配置逾時、重試機制 |
| 使用者不理解衝突 | 決策困難 | 提供詳細說明和建議 |

---

## 十一、後續階段規劃

### 第二階段：Migration 腳本產生

- 根據差異產生 ALTER TABLE 腳本
- 處理相依性順序
- 產生 Rollback 腳本

### 第三階段：自動化執行

- 腳本執行引擎
- 執行前驗證
- 執行後結構比對驗證

---

## 十二、參考資料

- 原始計畫文件：`docs/MSSQL_Migration_Plan.md`
- 現有專案架構：Clean Architecture + MVVM
- 現有技術棧：.NET 8, Avalonia, Dapper, ClosedXML

---

*此文件將隨開發進度持續更新*
