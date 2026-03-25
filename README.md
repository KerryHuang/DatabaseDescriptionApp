# Specurai - 資料庫規格查詢工具

Specurai 是一個跨平台桌面應用程式，用於查詢和管理 SQL Server 資料庫的結構規格，包含資料表、檢視表、預存程序和函數的詳細資訊。

## 功能特色

### 基本功能
- **多連線管理** - 支援儲存多組資料庫連線設定，可快速切換
- **物件瀏覽** - 樹狀結構顯示 Tables、Views、Stored Procedures、Functions
- **搜尋過濾** - 即時搜尋物件名稱和說明
- **MDI 多分頁介面** - 同時開啟多個文件視窗，支援分頁關閉按鈕
- **深色/淺色主題** - 支援主題切換

### 詳細資訊檢視
- **欄位資訊** - 名稱、完整型別（如 `varchar(50)`、`decimal(18,2)`）、主鍵、可空、預設值、說明
- **索引資訊** - 名稱、類型、唯一性、欄位、建立時間
- **索引管理** - 支援刪除索引操作
- **關聯資訊** - 外鍵約束、來源/目標表欄位
- **參數資訊** - 預存程序/函數的參數
- **定義** - 預存程序/函數的 SQL 程式碼
- **欄位說明編輯** - 直接編輯欄位說明並儲存
- **欄位搜尋** - 在物件詳細頁籤中搜尋欄位名稱

### SQL 查詢工具
- **SQL 查詢視窗** - 執行自訂 SQL 查詢 (Ctrl+Q)
- **結果匯出** - 將查詢結果匯出為 CSV

### 欄位搜尋與分析
- **欄位搜尋** - 搜尋資料庫中的欄位名稱 (Ctrl+F)
- **型態一致性檢查** - 偵測同名欄位在不同資料表中的型態差異
  - 綠色：完全一致
  - 黃色：警告（少數不一致）
  - 紅色：嚴重（多種型態或高比例不一致）
- **批次更新長度** - 一次更新所有不一致欄位的長度
- **套用說明** - 將選中欄位的說明套用至其他同名但說明為空的欄位（支援 TABLE 和 VIEW）

### 欄位統計
- **欄位使用分析** - 統計欄位在資料庫中的使用情況 (Ctrl+U)
- **型態一致性** - 分析同名欄位的型態分佈

### 資料表統計
- **資料表統計** - 檢視所有資料表的統計資訊 (Ctrl+T)
- **多維度篩選** - Schema、物件類型、資料表名稱、資料列數範圍、欄位數範圍
- **概估/精確列數** - 快速概估或精確計算資料列數
- **空間使用分析** - 資料大小、索引大小、總大小
- **圖表視覺化** - 資料列數排行長條圖、磁碟空間圓餅圖

### 匯出功能
- **Excel 匯出** - 將資料庫規格匯出為 Excel 檔案
- **跨平台** - 支援 Windows、macOS、Linux

### 備份與還原
- **資料庫備份** - 支援完整備份、差異備份、交易記錄備份
- **資料庫還原** - 支援覆蓋現有資料庫或還原為新資料庫
- **伺服器端操作** - 備份路徑為 SQL Server 伺服器端路徑，自動帶入預設路徑
- **備份驗證** - 自動驗證備份檔案的完整性
- **歷史記錄** - 保留備份歷史，可快速從歷史還原

### 結構比對
- **跨環境比對** - 比對不同環境（開發、測試、正式）的資料庫結構差異 (Ctrl+M)
- **差異分析** - 偵測新增、刪除、修改的物件
- **同步腳本** - 產生同步 SQL 腳本
- **匯出報表** - 匯出比對結果為 Excel 或 HTML

### 健康監控
- **伺服器健康監控** - 監控 CPU、記憶體、磁碟、連線數等系統資源 (Ctrl+H)
- **自動告警** - 當指標超過閾值時自動標記警告或危險狀態
- **趨勢分析** - 以圖表顯示歷史趨勢，分析資源使用模式
- **排程執行** - 透過 SQL Agent 作業每小時自動檢查

### 效能診斷
- **等候事件分析** - 分析 SQL Server 等候統計 (Ctrl+P)
- **耗時查詢** - 找出資源消耗最高的查詢
- **索引狀態** - 檢視索引使用效率與統計資訊
- **錯誤記錄** - 檢視 SQL Server 錯誤日誌

### 資料庫維護計劃
- **自動化設定** - 透過精靈建立資料庫維護計劃 (Ctrl+Shift+D)
- **前置檢查** - 自動檢查每個步驟的狀態，已完成的預設不執行
- **步驟管理** - 可選擇執行的項目：
  - 設定 Recovery Model 為 SIMPLE
  - 重新命名邏輯檔名
  - 建立登入帳號與使用者
  - 將使用者加入 db_owner
  - 建立每日全備份排程（可設定保留天數）
  - 建立每日還原排程（選填）
- **平台預設** - 支援 Windows/Linux 預設路徑，或自訂
- **Job 管理** - 檢視、啟用/停用、立即執行、修改排程、刪除 SQL Agent Job
- **執行歷史** - 選取 Job 即時顯示執行記錄和錯誤訊息
- **SQL 預覽** - 執行前可預覽完整 SQL 腳本

### 索引報表
- **缺少索引報表** - 分析 SQL Server 建議的缺少索引 (Ctrl+I)
  - 依改善指標排序，顯示嚴重度等級
  - 支援直接執行建立索引
  - 可依資料庫、資料表、改善指標篩選
- **未使用索引報表** - 找出未被使用但持續維護的索引 (Ctrl+J)
  - 分析索引維護成本
  - 支援直接刪除未使用索引

### MCP Server（AI 整合）
- **MCP 協定支援** - 透過 Model Context Protocol 讓 AI 助手直接存取資料庫結構
- **27 個工具** - 涵蓋連線管理、資料表查詢、SQL 執行、效能診斷等完整功能
- **stdio 傳輸** - 支援 Claude Code、Claude Desktop 等 MCP 客戶端
- **共用連線設定** - 與桌面應用程式共用 `connections.json` 連線設定

## 快捷鍵

| 快捷鍵 | 功能 |
|--------|------|
| Ctrl+L | 連線設定 |
| Ctrl+D | 切換深色/淺色主題 |
| Ctrl+Q | 開啟 SQL 查詢視窗 |
| Ctrl+F | 開啟欄位搜尋視窗 |
| Ctrl+Shift+B | 開啟備份與還原 |
| Ctrl+M | 開啟結構比對 |
| Ctrl+H | 開啟健康監控 |
| Ctrl+P | 開啟效能診斷 |
| Ctrl+I | 開啟缺少索引報表 |
| Ctrl+J | 開啟未使用索引報表 |
| Ctrl+U | 開啟欄位統計 |
| Ctrl+T | 開啟資料表統計 |
| Ctrl+Shift+D | 開啟資料庫維護計劃 |
| Ctrl+E | 匯出 Excel |
| Ctrl+Shift+E | 匯出連線設定 |
| Ctrl+Shift+I | 匯入連線設定 |
| Ctrl+B | 切換側邊欄 |
| Ctrl+W | 關閉目前分頁 |
| Ctrl+Shift+W | 關閉所有分頁 |
| F5 | 執行 SQL 查詢 |

## 技術架構

### 架構模式
- **Clean Architecture** - 分層架構，關注點分離
- **MVVM** - Model-View-ViewModel 模式
- **依賴注入** - Microsoft.Extensions.DependencyInjection

### 技術堆疊
| 層級 | 技術 |
|------|------|
| UI Framework | Avalonia UI 11.x |
| MVVM Toolkit | CommunityToolkit.Mvvm |
| MCP Server | ModelContextProtocol (C# SDK) |
| 資料庫存取 | Dapper + Microsoft.Data.SqlClient |
| Excel 匯出 | ClosedXML |
| 主題樣式 | Semi.Avalonia / Fluent Theme |

## 專案結構

```
DatabaseDescriptionApp/
├── src/
│   ├── Specurai.Domain/          # 領域層：實體、介面、列舉
│   │   ├── Entities/
│   │   │   ├── TableInfo.cs
│   │   │   ├── ColumnInfo.cs
│   │   │   ├── ColumnTypeInfo.cs
│   │   │   ├── ColumnUsageDetail.cs
│   │   │   ├── ColumnUsageStatistics.cs
│   │   │   ├── ConstraintInfo.cs
│   │   │   ├── IndexInfo.cs
│   │   │   ├── RelationInfo.cs
│   │   │   ├── ParameterInfo.cs
│   │   │   ├── ConnectionProfile.cs
│   │   │   ├── BackupHistory.cs
│   │   │   ├── BackupInfo.cs
│   │   │   ├── RestoreOptions.cs
│   │   │   ├── MissingIndex.cs
│   │   │   ├── UnusedIndex.cs
│   │   │   ├── IndexStatus.cs
│   │   │   ├── StatisticsInfo.cs
│   │   │   ├── TableStatisticsInfo.cs
│   │   │   ├── ExpensiveQuery.cs
│   │   │   ├── WaitStatistic.cs
│   │   │   ├── ErrorLogEntry.cs
│   │   │   ├── HealthLogEntry.cs
│   │   │   ├── HealthMetric.cs
│   │   │   ├── HealthStatusSummary.cs
│   │   │   ├── HealthMonitoringInstallStatus.cs
│   │   │   ├── MonitoringCategory.cs
│   │   │   ├── TrendDataPoint.cs
│   │   │   └── SchemaCompare/     # 結構比對相關實體
│   │   ├── Interfaces/
│   │   │   ├── ITableRepository.cs
│   │   │   ├── IColumnRepository.cs
│   │   │   ├── IColumnTypeRepository.cs
│   │   │   ├── IColumnUsageRepository.cs
│   │   │   ├── IIndexRepository.cs
│   │   │   ├── IRelationRepository.cs
│   │   │   ├── IParameterRepository.cs
│   │   │   ├── IBackupService.cs
│   │   │   ├── ISqlQueryRepository.cs
│   │   │   ├── ITableStatisticsRepository.cs
│   │   │   ├── IPerformanceDiagnosticsRepository.cs
│   │   │   ├── IHealthMonitoringRepository.cs
│   │   │   ├── IAgentJobRepository.cs
│   │   │   ├── IDatabaseInfoRepository.cs
│   │   │   └── ISchemaCollector.cs
│   │   └── Enums/
│   │       ├── BackupType.cs
│   │       ├── RestoreMode.cs
│   │       ├── DifferenceType.cs
│   │       ├── RiskLevel.cs
│   │       ├── SyncAction.cs
│   │       └── MaintenancePlanStep.cs
│   │
│   ├── Specurai.Application/     # 應用層：服務介面與實作
│   │   └── Services/
│   │       ├── ITableQueryService.cs
│   │       ├── TableQueryService.cs
│   │       ├── IConnectionManager.cs
│   │       ├── IExportService.cs
│   │       ├── IBackupService.cs
│   │       ├── IColumnUsageService.cs
│   │       ├── ColumnUsageService.cs
│   │       ├── ITableStatisticsService.cs
│   │       ├── TableStatisticsService.cs
│   │       ├── IPerformanceDiagnosticsService.cs
│   │       ├── PerformanceDiagnosticsService.cs
│   │       ├── ISchemaCompareService.cs
│   │       ├── SchemaCompareService.cs
│   │       ├── IHealthMonitoringService.cs
│   │       ├── HealthMonitoringService.cs
│   │       ├── IHealthMonitoringInstaller.cs
│   │       ├── IMaintenancePlanService.cs
│   │       ├── MaintenancePlanService.cs
│   │       ├── IMaintenancePlanSqlGenerator.cs
│   │       ├── IAgentJobService.cs
│   │       └── AgentJobService.cs
│   │
│   ├── Specurai.Infrastructure/  # 基礎設施層：資料存取實作
│   │   ├── Repositories/
│   │   │   ├── TableRepository.cs
│   │   │   ├── ColumnRepository.cs
│   │   │   ├── ColumnTypeRepository.cs
│   │   │   ├── ColumnUsageRepository.cs
│   │   │   ├── IndexRepository.cs
│   │   │   ├── RelationRepository.cs
│   │   │   ├── ParameterRepository.cs
│   │   │   ├── SqlQueryRepository.cs
│   │   │   ├── TableStatisticsRepository.cs
│   │   │   ├── PerformanceDiagnosticsRepository.cs
│   │   │   ├── HealthMonitoringRepository.cs
│   │   │   ├── AgentJobRepository.cs
│   │   │   └── DatabaseInfoRepository.cs
│   │   ├── Services/
│   │   │   ├── ConnectionManager.cs
│   │   │   ├── ExcelExportService.cs
│   │   │   ├── MssqlBackupService.cs
│   │   │   ├── HealthMonitoringInstaller.cs
│   │   │   └── MaintenancePlanSqlGenerator.cs
│   │   └── Scripts/
│   │       ├── HealthMonitoringInstall.sql
│   │       ├── HealthMonitoringUninstall.sql
│   │       └── SyncScriptGenerator.cs
│   │
│   ├── Specurai.McpServer/       # MCP Server：AI 整合
│   │   ├── Program.cs
│   │   └── Tools/
│   │       ├── ConnectionTools.cs
│   │       ├── TableTools.cs
│   │       ├── SqlTools.cs
│   │       ├── DescriptionTools.cs
│   │       ├── PerformanceTools.cs
│   │       ├── HealthTools.cs
│   │       └── StatisticsTools.cs
│   │
│   └── Specurai.Desktop/         # 桌面應用層：UI
│       ├── Views/
│       │   ├── MainWindow.axaml
│       │   ├── ConnectionSetupWindow.axaml
│       │   ├── ConfirmDialog.axaml
│       │   ├── TableDetailDocumentView.axaml
│       │   ├── SqlQueryDocumentView.axaml
│       │   ├── ColumnSearchDocumentView.axaml
│       │   ├── ColumnUsageDocumentView.axaml
│       │   ├── TableStatisticsDocumentView.axaml
│       │   ├── BackupRestoreDocumentView.axaml
│       │   ├── SchemaCompareDocumentView.axaml
│       │   ├── HealthMonitoringDocumentView.axaml
│       │   ├── PerformanceDiagnosticsDocumentView.axaml
│       │   ├── MissingIndexReportDocumentView.axaml
│       │   ├── UnusedIndexReportDocumentView.axaml
│       │   ├── MaintenancePlanDocumentView.axaml
│       │   ├── ScheduleEditWindow.axaml
│       │   └── AboutDocumentView.axaml
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs
│       │   ├── ViewModelBase.cs
│       │   ├── ObjectTreeViewModel.cs
│       │   ├── DocumentViewModel.cs
│       │   ├── ConnectionSetupViewModel.cs
│       │   ├── TableDetailDocumentViewModel.cs
│       │   ├── SqlQueryDocumentViewModel.cs
│       │   ├── ColumnSearchDocumentViewModel.cs
│       │   ├── ColumnTypeGroupViewModel.cs
│       │   ├── ColumnUsageDocumentViewModel.cs
│       │   ├── TableStatisticsDocumentViewModel.cs
│       │   ├── BackupRestoreDocumentViewModel.cs
│       │   ├── SchemaCompareDocumentViewModel.cs
│       │   ├── HealthMonitoringDocumentViewModel.cs
│       │   ├── PerformanceDiagnosticsDocumentViewModel.cs
│       │   ├── MissingIndexReportDocumentViewModel.cs
│       │   ├── UnusedIndexReportDocumentViewModel.cs
│       │   ├── MaintenancePlanDocumentViewModel.cs
│       │   ├── ScheduleEditViewModel.cs
│       │   └── AboutDocumentViewModel.cs
│       ├── Converters/
│       │   ├── ConsistencyLevelConverters.cs
│       │   ├── ColumnUsageConverters.cs
│       │   ├── HealthMonitoringConverters.cs
│       │   ├── SchemaCompareConverters.cs
│       │   └── TestResultColorConverter.cs
│       └── Program.cs
│
├── tests/
│   ├── Specurai.Domain.Tests/
│   ├── Specurai.Application.Tests/
│   ├── Specurai.Infrastructure.Tests/
│   └── Specurai.Desktop.Tests/
│
└── docs/
    ├── UserGuide.md
    └── plans/
```

## 下載

從 [GitHub Releases](https://github.com/KerryHuang/DatabaseDescriptionApp/releases) 下載最新版本：

| 平台 | 下載連結 | 說明 |
|------|----------|------|
| Windows x64 | [Specurai-win-Setup.exe](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest/download/Specurai-win-Setup.exe) | 安裝程式 |
| Windows x64 | [Specurai-win-Portable.zip](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest/download/Specurai-win-Portable.zip) | 可攜式版本 |
| macOS (Apple Silicon) | [Specurai-osx-arm64.zip](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest) | 從 Releases 頁面下載 |
| Linux x64 | [Specurai.AppImage](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest/download/Specurai.AppImage) | AppImage 格式 |

> Windows 安裝程式會自動建立開始選單和桌面捷徑，並支援自動更新。

## 系統需求

### 執行預編譯版本
- Windows 10/11、macOS 11+、或 Linux（glibc 2.17+）
- SQL Server 2008 或更高版本

### 從原始碼建置
- .NET 8.0 SDK 或更高版本
- SQL Server 2008 或更高版本（支援 Windows 驗證或 SQL Server 驗證）

## 建置與執行

### 建置專案
```bash
dotnet build
```

### 執行應用程式
```bash
dotnet run --project src/Specurai.Desktop/Specurai.Desktop.csproj
```

### 執行測試
```bash
dotnet test
```

### 發布單一執行檔

**Windows:**
```bash
dotnet publish src/Specurai.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

**macOS (Apple Silicon):**
```bash
dotnet publish src/Specurai.Desktop -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

**macOS (Intel):**
```bash
dotnet publish src/Specurai.Desktop -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true
```

**Linux:**
```bash
dotnet publish src/Specurai.Desktop -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

## 使用說明

### 1. 設定連線
1. 點擊「設定連線」按鈕或按 Ctrl+L
2. 點擊「新增」建立新連線
3. 輸入連線資訊（伺服器、資料庫、驗證方式）
4. 點擊「測試連線」確認連線成功
5. 點擊「儲存」保存設定
6. 點擊「連線」或關閉視窗後從下拉選單選擇連線

### 2. 瀏覽物件
- 左側樹狀結構顯示所有資料庫物件
- 使用搜尋框過濾物件
- 雙擊物件查看詳細資訊

### 3. 查看詳細資訊
- **欄位** - 顯示欄位定義（含完整型別），可編輯說明，支援欄位搜尋
- **索引** - 顯示索引資訊、建立時間，支援刪除索引（僅資料表）
- **關聯** - 顯示外鍵關聯（僅資料表）
- **參數** - 顯示參數定義（預存程序/函數）
- **定義** - 顯示 SQL 程式碼（預存程序/函數）

### 4. SQL 查詢
1. 按 Ctrl+Q 或選單「工具 > SQL 查詢」
2. 輸入 SQL 查詢語句
3. 點擊「執行」或按 F5
4. 可匯出結果為 CSV

### 5. 欄位搜尋與一致性分析
1. 按 Ctrl+F 或選單「工具 > 欄位搜尋」
2. 輸入欄位名稱關鍵字，點擊「搜尋」
3. 點擊「分析一致性」檢查同名欄位的型態差異
4. 選擇不一致的欄位群組，可批次更新長度
5. 選中有說明的欄位，可套用說明至其他空白欄位

### 6. 欄位統計
1. 按 Ctrl+U 或選單「工具 > 欄位統計」
2. 檢視欄位在各資料表中的使用情況與型態分佈

### 7. 資料表統計
1. 按 Ctrl+T 或選單「工具 > 資料表統計」
2. 檢視所有資料表的統計資訊（資料列數、空間使用、欄位數等）
3. 使用篩選條件縮小範圍
4. 點擊「精確列數」取得準確的資料列數

### 8. 匯出 Excel
- 點擊「匯出 Excel」按鈕
- 選擇儲存位置
- 匯出包含所有物件規格的 Excel 檔案

### 9. 備份與還原
1. 按 Ctrl+B 或選單「工具 > 備份與還原」
2. **備份**：選擇連線、備份類型（完整/差異/交易記錄），設定儲存路徑後點擊「備份」
3. **還原**：選擇備份檔案，選擇覆蓋現有或建立新資料庫，點擊「還原」
4. **歷史記錄**：從歷史分頁可快速檢視過去的備份，並可直接還原

### 10. 結構比對
1. 按 Ctrl+M 或選單「工具 > 結構比對」
2. 選擇來源和目標資料庫連線
3. 執行比對，檢視結構差異
4. 可產生同步 SQL 腳本或匯出比對報表

### 11. 健康監控
1. 選單「工具 > 健康監控」或按 Ctrl+H
2. 首次使用需要安裝監控系統（會在目標伺服器建立 DBA 資料庫）
3. 安裝完成後可檢視：
   - **總覽**：各監控類型的狀態摘要卡片
   - **即時指標**：所有監控指標的詳細資料
   - **告警**：最近的警告和危險狀態紀錄
   - **趨勢**：歷史趨勢圖表
   - **監控設定**：管理監控類別的啟用狀態和檢查間隔

### 12. 效能診斷
1. 按 Ctrl+P 或選單「工具 > 效能診斷」
2. 檢視等候事件統計、耗時查詢、索引狀態、錯誤日誌

### 13. 缺少索引報表
1. 按 Ctrl+I 或選單「工具 > 缺少索引報表」
2. 檢視 SQL Server 建議的缺少索引，依嚴重度和改善指標排序
3. 可依資料庫、資料表篩選
4. 點擊「建立索引」直接執行建立

### 14. 資料庫維護計劃
1. 按 Ctrl+Shift+D 或選單「工具 > 資料庫維護計劃」
2. **管理面板**：檢視現有的 SQL Agent Job，選取 Job 可查看執行歷史和錯誤訊息
3. **新增計劃**：點擊「新增計劃」開啟精靈
   - 步驟 1：基本設定（資料庫自動帶入、選擇平台、設定路徑和帳號密碼、保留天數）
   - 步驟 2：選擇執行項目（自動檢查每個步驟狀態，已完成的不勾選）
   - 步驟 3：確認與執行（可預覽 SQL、檢視檢查結果、執行）
4. **Job 操作**：啟用/停用、立即執行、修改排程、刪除

### 15. 未使用索引報表
1. 按 Ctrl+J 或選單「工具 > 未使用索引報表」

2. 檢視未被使用但持續維護的索引
3. 可直接刪除不需要的索引以節省資源

## CLI 命令列工具

Specurai CLI 提供完整的命令列介面，適合自動化腳本、CI/CD 管線和 AI Agent 使用。

### 安裝

```bash
# dotnet tool 安裝
dotnet tool install -g Specurai.Cli

# 或從原始碼建置
dotnet run --project src/Specurai.Cli
```

### 連線設定

```bash
# 互動式新增連線
specurai conn add

# 參數式新增
specurai conn add --name "正式環境" --server 192.168.1.100 --database MyDB --user sa --password P@ss

# 列出所有連線
specurai conn list

# 切換目前連線
specurai conn switch "正式環境"

# 測試連線
specurai conn test

# 也可直接用參數執行，不需先新增連線
specurai --server localhost --database MyDB --user sa --password P@ss tables list

# 或用環境變數
export SPECURAI_SERVER=localhost
export SPECURAI_DATABASE=MyDB
specurai tables list

# 從 stdin 匯入連線（支援 JSON 格式）
echo '{"server":"localhost","database":"MyDB","user":"sa","password":"P@ss"}' | specurai conn import --stdin
```

### 常用命令

```bash
# 物件瀏覽
specurai tables list                          # 列出所有物件
specurai tables list --type TABLE             # 只列資料表
specurai tables columns dbo.Users             # 顯示欄位
specurai tables indexes dbo.Users             # 顯示索引
specurai tables definition dbo.GetUser        # 顯示 SP 原始碼

# 描述編輯
specurai describe table dbo.Users "使用者資料表"
specurai describe column dbo.Users.Email "電子郵件地址"

# SQL 查詢
specurai sql query "SELECT TOP 10 * FROM dbo.Users"
specurai sql search-columns Email             # 搜尋欄位名稱
specurai sql search-columns Email --all-profiles  # 跨所有資料庫搜尋

# 匯出
specurai export excel                         # 匯出所有表格到 Excel
specurai export excel --table dbo.Users       # 匯出單一表格

# 效能診斷
specurai perf waits                           # 等候事件統計
specurai perf queries --top 10                # 耗時查詢
specurai perf missing-indexes                 # 缺少索引建議
specurai perf unused-indexes                  # 未使用索引

# 健康監控
specurai health status                        # 健康狀態摘要
specurai health metrics                       # 目前指標
specurai health alerts --days 7               # 最近警示

# Schema 比對（跨資料庫）
specurai schema compare --base "正式環境" --target "測試環境"
specurai schema compare-multi --base "正式環境" --targets "客戶A,客戶B,客戶C"

# 使用分析
specurai usage scan --years 2                 # 掃描閒置物件
specurai usage compare --base "正式環境" --targets "客戶A,客戶B"

# Agent Job 管理
specurai jobs list                            # 列出排程工作
specurai jobs start <jobId>                   # 立即執行
```

### JSON 輸出（AI Agent 友善）

所有命令支援 `--json` 旗標，回傳結構化 JSON：

```bash
specurai --json tables list
# {
#   "success": true,
#   "data": [
#     { "schema": "dbo", "name": "Users", "type": "BASE TABLE", "description": "..." }
#   ],
#   "metadata": { "count": 42 }
# }

specurai --json perf waits --top 5
specurai --json schema compare --base "正式環境" --target "測試環境"
```

### 外部工具整合

Specurai CLI 提供多種方式接受外部連線，不綁定任何特定工具：

```bash
# 方式 1：CLI 參數
specurai --server $HOST --database $DB --user $USER --password $PASS tables list

# 方式 2：環境變數
export SPECURAI_SERVER=192.168.1.100
export SPECURAI_DATABASE=MyDB
specurai tables list

# 方式 3：連線字串
specurai --connection-string "Data Source=...;Initial Catalog=..." tables list

# 方式 4：stdin JSON（相容 mpe show --json 的 mssql 格式）
echo '{"mssql":{"host":"192.168.1.100","port":"1433","userId":"sa","password":"p","applicationDatabase":"db"}}' \
  | specurai conn import --stdin
```

若你的環境使用 [mpe](https://github.com/example/mp-env)（MoldPlan 環境設定 CLI）管理資料庫連線，可直接串接：

```bash
# 從 mpe 匯入客戶環境連線
mpe show junhe-staging --json | specurai conn import --stdin
mpe show junhe-prod --json | specurai conn import --stdin

# 匯入後即可進行跨環境 Schema 比對
specurai schema compare-multi --base "均賀 Staging" --targets "均賀 Production"
```

## MCP Server

Specurai MCP Server 讓 AI 助手（如 Claude Code、Claude Desktop）透過 [Model Context Protocol](https://modelcontextprotocol.io/) 直接存取資料庫結構資訊。

### 架構

```
Domain → Application → Infrastructure
                    ↘ Desktop (Avalonia UI)
                    ↘ McpServer (stdio console app)
                    ↘ Cli (命令列工具)
```

MCP Server、CLI 與桌面應用程式處於相同的架構層級，共用 Domain、Application、Infrastructure 三層的服務。

### 安裝 MCP Server

> **完整安裝指引：** 請參閱 [docs/INSTALL.md](docs/INSTALL.md)，包含從零開始的完整安裝步驟。AI 助手可直接讀取該文件引導使用者完成安裝。

支援 Claude Code、Claude Desktop、Cursor、Windsurf 等所有 MCP 客戶端。

#### 方式一：dotnet tool 安裝（推薦）

**前置需求：** 安裝 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

| 平台 | 安裝方式 |
|------|----------|
| Windows | 從官網下載安裝程式，或執行 `winget install Microsoft.DotNet.SDK.8` |
| macOS | 執行 `brew install dotnet@8`，或從官網下載安裝 |
| Linux (Ubuntu/Debian) | 執行 `sudo apt install dotnet-sdk-8.0` |
| Linux (Fedora) | 執行 `sudo dnf install dotnet-sdk-8.0` |

**安裝 MCP Server：**

```bash
dotnet tool install -g Specurai.McpServer
```

> **macOS / Linux 注意：** 若出現 PATH 警告，需將 dotnet tools 加入 PATH：
>
> ```bash
> # macOS (zsh)
> echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.zprofile
> source ~/.zprofile
>
> # Linux (bash)
> echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
> source ~/.bashrc
> ```

安裝完成後，`specurai-mcp` 指令即可在終端機中使用。

**更新版本：**

```bash
dotnet tool update -g Specurai.McpServer
```

#### 方式二：下載獨立執行檔

不需安裝 .NET，從 [GitHub Releases](https://github.com/KerryHuang/DatabaseDescriptionApp/releases) 下載對應平台的檔案：

| 平台 | 檔案 |
|------|------|
| Windows x64 | `Specurai.McpServer-win-x64.zip` |
| macOS Apple Silicon | `Specurai.McpServer-osx-arm64.tar.gz` |
| macOS Intel | `Specurai.McpServer-osx-x64.tar.gz` |
| Linux x64 | `Specurai.McpServer-linux-x64.tar.gz` |

解壓後記下執行檔的完整路徑，下一步設定時會用到。

#### 設定 MCP 客戶端

##### Claude Code

```bash
# dotnet tool 安裝
claude mcp add specurai -s user -- specurai-mcp

# 獨立執行檔（請替換為實際路徑）
# Windows:  claude mcp add specurai -s user -- C:\路徑\Specurai.McpServer.exe
# macOS:    claude mcp add specurai -s user -- /路徑/Specurai.McpServer
```

##### Claude Desktop / Cursor / Windsurf

開啟對應的設定檔，加入以下內容：

| 客戶端 | Windows | macOS |
|--------|---------|-------|
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Cursor | `%APPDATA%\Cursor\mcp.json` | `~/Library/Application Support/Cursor/mcp.json` |
| Windsurf | `%APPDATA%\Windsurf\mcp_config.json` | `~/Library/Application Support/Windsurf/mcp_config.json` |

**dotnet tool 安裝：**

```json
{
  "mcpServers": {
    "specurai": {
      "command": "specurai-mcp"
    }
  }
}
```

**獨立執行檔（請替換為實際路徑）：**

```json
{
  "mcpServers": {
    "specurai": {
      "command": "/完整路徑/Specurai.McpServer"
    }
  }
}
```

> **注意：** Windows 路徑使用 `\\` 或 `/`，且執行檔名為 `Specurai.McpServer.exe`。

#### 驗證安裝

在 AI 客戶端中輸入：

```
列出所有連線設定
```

若顯示連線清單，即表示安裝成功。

### 連線設定

MCP Server 與桌面應用程式共用連線設定，不需要額外設定：

| 平台 | 設定檔位置 |
|------|-----------|
| Windows | `%APPDATA%\Specurai\connections.json` |
| macOS | `~/.config/Specurai/connections.json` |
| Linux | `~/.config/Specurai/connections.json` |

在桌面應用程式中新增的連線設定，MCP Server 可直接使用。

### 可用工具一覽（50 個）

#### 連線管理
| 工具 | 說明 |
|------|------|
| `list_connections` | 列出所有已設定的連線設定檔 |
| `switch_connection` | 切換至指定的連線（依名稱或 ID） |
| `test_connection` | 測試目前的連線是否正常 |
| `add_connection` | 新增資料庫連線設定 ⚠️ |
| `update_connection` | 更新現有的連線設定 ⚠️ |
| `delete_connection` | 刪除連線設定 ⚠️ |
| `export_connections` | 匯出連線設定為 JSON 檔案 |
| `import_connections` | 從 JSON 檔案匯入連線設定 ⚠️ |

#### 資料表查詢
| 工具 | 說明 |
|------|------|
| `list_tables` | 列出資料庫物件（可依類型篩選：BASE TABLE、VIEW、PROCEDURE、FUNCTION） |
| `get_columns` | 取得欄位資訊（型別、主鍵、可空、描述等） |
| `get_indexes` | 取得索引資訊 |
| `get_relations` | 取得外鍵關聯 |
| `get_parameters` | 取得預存程序/函數參數 |
| `get_definition` | 取得預存程序/函數 SQL 定義 |

#### SQL 查詢
| 工具 | 說明 |
|------|------|
| `execute_readonly_sql` | 執行唯讀 SQL 查詢 |
| `search_columns` | 搜尋欄位名稱（模糊/精確比對） |
| `search_columns_multi_database` | 在多個資料庫中同時搜尋欄位名稱 |
| `get_create_table_sql` | 產生 CREATE TABLE 語句 |

#### 描述管理
| 工具 | 說明 |
|------|------|
| `update_table_description` | 更新資料表/檢視/預存程序的描述 |
| `update_column_description` | 更新欄位描述 |

#### 效能診斷
| 工具 | 說明 |
|------|------|
| `get_wait_statistics` | 等候事件統計 |
| `get_expensive_queries` | 最耗時的查詢 |
| `get_expensive_procedures` | 最耗時的預存程序 |
| `get_missing_indexes` | 缺少索引建議 |
| `get_unused_indexes` | 未使用索引清單 |
| `get_error_log` | SQL Server 錯誤記錄 |

#### 健康監控
| 工具 | 說明 |
|------|------|
| `get_health_install_status` | 健康監控系統安裝狀態 |
| `get_health_status` | 健康狀態摘要 |
| `get_health_metrics` | 目前健康指標數值 |
| `get_health_alerts` | 最近告警記錄 |
| `install_health_monitoring` | 安裝健康監控系統 ⚠️ |
| `uninstall_health_monitoring` | 移除健康監控系統 ⚠️ |
| `export_health_monitoring_sql` | 產生健康監控安裝 SQL 腳本 |

#### 統計資訊
| 工具 | 說明 |
|------|------|
| `get_table_statistics` | 資料表統計（列數、大小） |
| `get_exact_row_count` | 精確列數（COUNT(*)） |
| `get_column_usage_statistics` | 欄位使用狀態統計 |

#### Agent Job 管理
| 工具 | 說明 |
|------|------|
| `list_agent_jobs` | 列出 Specurai 管理的 Agent Job |
| `list_non_specurai_jobs` | 列出未管理的 Agent Job |
| `get_agent_job_history` | 取得 Job 執行歷史紀錄 |
| `set_agent_job_enabled` | 啟用/停用 Agent Job ⚠️ |
| `start_agent_job` | 立即執行 Agent Job ⚠️ |
| `delete_agent_job` | 刪除 Agent Job ⚠️ |
| `update_agent_job_schedule` | 更新 Job 排程設定 ⚠️ |
| `import_agent_job` | 匯入 Job 至 Specurai 管理 ⚠️ |

#### Schema 比對
| 工具 | 說明 |
|------|------|
| `compare_schemas` | 比對兩個資料庫的 Schema 差異 |
| `compare_multiple_schemas` | 比對一對多資料庫的 Schema 差異 |

#### 使用狀態分析
| 工具 | 說明 |
|------|------|
| `scan_usage` | 掃描資料表/欄位使用狀態 |
| `compare_usage_multi_environment` | 多環境使用狀態比對 |
| `generate_drop_table_sql` | 產生 DROP TABLE SQL（不執行） |
| `generate_drop_column_sql` | 產生 DROP COLUMN SQL（不執行） |

#### 維護計劃
| 工具 | 說明 |
|------|------|
| `check_maintenance_prerequisites` | 檢查維護計劃前置條件 |
| `check_maintenance_steps` | 檢查維護計劃各步驟狀態 |
| `generate_maintenance_plan_sql` | 產生維護計劃預覽 SQL |
| `execute_maintenance_plan` | 執行維護計劃 ⚠️ |

#### 匯出
| 工具 | 說明 |
|------|------|
| `export_all_to_excel` | 匯出所有資料表規格為 Excel |
| `export_table_to_excel` | 匯出指定資料表規格為 Excel |

> ⚠️ 標記表示寫入或破壞性操作

### 使用範例

在 Claude Code 中直接詢問 AI，它會自動呼叫對應的 MCP 工具：

- 「列出所有資料表」→ `list_tables`
- 「查看 Orders 表的欄位」→ `get_columns`
- 「找出所有包含 Price 的欄位」→ `search_columns`
- 「執行 SELECT TOP 10 * FROM Users」→ `execute_readonly_sql`
- 「分析資料庫效能瓶頸」→ `get_wait_statistics` + `get_expensive_queries`

## 連線設定儲存位置

連線設定儲存於使用者 AppData 目錄：
- **Windows:** `%APPDATA%\Specurai\connections.json`
- **macOS:** `~/.config/Specurai/connections.json`
- **Linux:** `~/.config/Specurai/connections.json`

## 螢幕截圖

### 主畫面
- 左側：物件樹狀結構
- 右側：MDI 多分頁文件區域

### 欄位型態一致性分析
- 左側：欄位群組清單（顯示一致性等級）
- 右側：選中群組的詳細資訊（可篩選、排序、批次更新）

## 授權條款

MIT License

## 貢獻

歡迎提交 Issue 和 Pull Request。
