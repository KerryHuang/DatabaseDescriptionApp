# TableSpec - 資料庫規格查詢工具

TableSpec 是一個跨平台桌面應用程式，用於查詢和管理 SQL Server 資料庫的結構規格，包含資料表、檢視表、預存程序和函數的詳細資訊。

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

### 索引報表
- **缺少索引報表** - 分析 SQL Server 建議的缺少索引 (Ctrl+I)
  - 依改善指標排序，顯示嚴重度等級
  - 支援直接執行建立索引
  - 可依資料庫、資料表、改善指標篩選
- **未使用索引報表** - 找出未被使用但持續維護的索引 (Ctrl+J)
  - 分析索引維護成本
  - 支援直接刪除未使用索引

## 快捷鍵

| 快捷鍵 | 功能 |
|--------|------|
| Ctrl+L | 連線設定 |
| Ctrl+Q | 開啟 SQL 查詢視窗 |
| Ctrl+F | 開啟欄位搜尋視窗 |
| Ctrl+B | 開啟備份與還原 |
| Ctrl+M | 開啟結構比對 |
| Ctrl+H | 開啟健康監控 |
| Ctrl+P | 開啟效能診斷 |
| Ctrl+I | 開啟缺少索引報表 |
| Ctrl+J | 開啟未使用索引報表 |
| Ctrl+U | 開啟欄位統計 |
| Ctrl+T | 開啟資料表統計 |
| Ctrl+W | 關閉目前分頁 |
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
| 資料庫存取 | Dapper + Microsoft.Data.SqlClient |
| Excel 匯出 | ClosedXML |
| 主題樣式 | Semi.Avalonia / Fluent Theme |

## 專案結構

```
DatabaseDescriptionApp/
├── src/
│   ├── TableSpec.Domain/          # 領域層：實體、介面、列舉
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
│   │   │   └── ISchemaCollector.cs
│   │   └── Enums/
│   │       ├── BackupType.cs
│   │       ├── RestoreMode.cs
│   │       ├── DifferenceType.cs
│   │       ├── RiskLevel.cs
│   │       └── SyncAction.cs
│   │
│   ├── TableSpec.Application/     # 應用層：服務介面與實作
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
│   │       └── IHealthMonitoringInstaller.cs
│   │
│   ├── TableSpec.Infrastructure/  # 基礎設施層：資料存取實作
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
│   │   │   └── HealthMonitoringRepository.cs
│   │   ├── Services/
│   │   │   ├── ConnectionManager.cs
│   │   │   ├── ExcelExportService.cs
│   │   │   ├── MssqlBackupService.cs
│   │   │   └── HealthMonitoringInstaller.cs
│   │   └── Scripts/
│   │       ├── HealthMonitoringInstall.sql
│   │       ├── HealthMonitoringUninstall.sql
│   │       └── SyncScriptGenerator.cs
│   │
│   └── TableSpec.Desktop/         # 桌面應用層：UI
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
│   ├── TableSpec.Domain.Tests/
│   ├── TableSpec.Application.Tests/
│   ├── TableSpec.Infrastructure.Tests/
│   └── TableSpec.Desktop.Tests/
│
└── docs/
    ├── UserGuide.md
    └── plans/
```

## 下載

從 [GitHub Releases](https://github.com/KerryHuang/DatabaseDescriptionApp/releases) 下載最新版本：

| 平台 | 下載連結 | 說明 |
|------|----------|------|
| Windows x64 | [TableSpec-win-Setup.exe](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest/download/TableSpec-win-Setup.exe) | 安裝程式 |
| Windows x64 | [TableSpec-win-Portable.zip](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest/download/TableSpec-win-Portable.zip) | 可攜式版本 |
| macOS (Apple Silicon) | [TableSpec-osx-arm64.zip](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest) | 從 Releases 頁面下載 |
| Linux x64 | [TableSpec.AppImage](https://github.com/KerryHuang/DatabaseDescriptionApp/releases/latest/download/TableSpec.AppImage) | AppImage 格式 |

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
dotnet run --project src/TableSpec.Desktop/TableSpec.Desktop.csproj
```

### 執行測試
```bash
dotnet test
```

### 發布單一執行檔

**Windows:**
```bash
dotnet publish src/TableSpec.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

**macOS (Apple Silicon):**
```bash
dotnet publish src/TableSpec.Desktop -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

**macOS (Intel):**
```bash
dotnet publish src/TableSpec.Desktop -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true
```

**Linux:**
```bash
dotnet publish src/TableSpec.Desktop -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
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

### 14. 未使用索引報表
1. 按 Ctrl+J 或選單「工具 > 未使用索引報表」
2. 檢視未被使用但持續維護的索引
3. 可直接刪除不需要的索引以節省資源

## 連線設定儲存位置

連線設定儲存於使用者 AppData 目錄：
- **Windows:** `%APPDATA%\TableSpec\connections.json`
- **macOS:** `~/.config/TableSpec/connections.json`
- **Linux:** `~/.config/TableSpec/connections.json`

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
