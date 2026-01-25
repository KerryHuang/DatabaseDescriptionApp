# TableSpec - 資料庫規格查詢工具

TableSpec 是一個跨平台桌面應用程式，用於查詢和管理 SQL Server 資料庫的結構規格，包含資料表、檢視表、預存程序和函數的詳細資訊。

## 功能特色

### 基本功能
- **多連線管理** - 支援儲存多組資料庫連線設定，可快速切換
- **物件瀏覽** - 樹狀結構顯示 Tables、Views、Stored Procedures、Functions
- **搜尋過濾** - 即時搜尋物件名稱和說明
- **MDI 多分頁介面** - 同時開啟多個文件視窗，支援分頁關閉按鈕
- **深色/淺色主題** - 支援主題切換 (Ctrl+T)

### 詳細資訊檢視
- **欄位資訊** - 名稱、型別、長度、主鍵、可空、預設值、說明
- **索引資訊** - 名稱、類型、唯一性、欄位
- **關聯資訊** - 外鍵約束、來源/目標表欄位
- **參數資訊** - 預存程序/函數的參數
- **定義** - 預存程序/函數的 SQL 程式碼
- **欄位說明編輯** - 直接編輯欄位說明並儲存

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

### 匯出功能
- **Excel 匯出** - 將資料庫規格匯出為 Excel 檔案
- **跨平台** - 支援 Windows、macOS、Linux

### 備份與還原
- **資料庫備份** - 支援完整備份、差異備份、交易記錄備份
- **資料庫還原** - 支援覆蓋現有資料庫或還原為新資料庫
- **備份驗證** - 自動驗證備份檔案的完整性
- **歷史記錄** - 保留備份歷史，可快速從歷史還原

### 健康監控
- **伺服器健康監控** - 監控 CPU、記憶體、磁碟、連線數等系統資源
- **自動告警** - 當指標超過閾值時自動標記警告或危險狀態
- **趨勢分析** - 以圖表顯示歷史趨勢，分析資源使用模式
- **排程執行** - 透過 SQL Agent 作業每小時自動檢查

## 快捷鍵

| 快捷鍵 | 功能 |
|--------|------|
| Ctrl+Q | 開啟 SQL 查詢視窗 |
| Ctrl+F | 開啟欄位搜尋視窗 |
| Ctrl+B | 開啟備份與還原 |
| Ctrl+W | 關閉目前分頁 |
| Ctrl+T | 切換深色/淺色主題 |

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
│   ├── TableSpec.Domain/          # 領域層：實體、介面
│   │   ├── Entities/
│   │   │   ├── TableInfo.cs
│   │   │   ├── ColumnInfo.cs
│   │   │   ├── ColumnTypeInfo.cs
│   │   │   ├── ConstraintInfo.cs
│   │   │   ├── IndexInfo.cs
│   │   │   ├── RelationInfo.cs
│   │   │   ├── ParameterInfo.cs
│   │   │   └── ConnectionProfile.cs
│   │   └── Interfaces/
│   │       ├── ITableRepository.cs
│   │       ├── IColumnRepository.cs
│   │       ├── IColumnTypeRepository.cs
│   │       ├── IIndexRepository.cs
│   │       ├── IRelationRepository.cs
│   │       ├── IParameterRepository.cs
│   │       ├── IBackupRepository.cs
│   │       └── IHealthMonitoringRepository.cs
│   │
│   ├── TableSpec.Application/     # 應用層：服務介面與實作
│   │   └── Services/
│   │       ├── ITableQueryService.cs
│   │       ├── TableQueryService.cs
│   │       ├── IConnectionManager.cs
│   │       ├── IExportService.cs
│   │       ├── IBackupService.cs
│   │       ├── IHealthMonitoringService.cs
│   │       └── HealthMonitoringService.cs
│   │
│   ├── TableSpec.Infrastructure/  # 基礎設施層：資料存取實作
│   │   ├── Repositories/
│   │   │   ├── TableRepository.cs
│   │   │   ├── ColumnRepository.cs
│   │   │   ├── ColumnTypeRepository.cs
│   │   │   ├── IndexRepository.cs
│   │   │   ├── RelationRepository.cs
│   │   │   ├── ParameterRepository.cs
│   │   │   └── HealthMonitoringRepository.cs
│   │   ├── Services/
│   │   │   ├── ConnectionManager.cs
│   │   │   ├── ExcelExportService.cs
│   │   │   ├── BackupService.cs
│   │   │   └── HealthMonitoringInstaller.cs
│   │   └── Scripts/
│   │       ├── HealthMonitoringInstall.sql
│   │       └── HealthMonitoringUninstall.sql
│   │
│   └── TableSpec.Desktop/         # 桌面應用層：UI
│       ├── Views/
│       │   ├── MainWindow.axaml
│       │   ├── ConnectionSetupWindow.axaml
│       │   ├── TableDetailDocumentView.axaml
│       │   ├── SqlQueryDocumentView.axaml
│       │   ├── ColumnSearchDocumentView.axaml
│       │   ├── BackupRestoreDocumentView.axaml
│       │   └── HealthMonitoringDocumentView.axaml
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs
│       │   ├── DocumentViewModel.cs
│       │   ├── TableDetailDocumentViewModel.cs
│       │   ├── SqlQueryDocumentViewModel.cs
│       │   ├── ColumnSearchDocumentViewModel.cs
│       │   ├── ConnectionSetupViewModel.cs
│       │   ├── BackupRestoreDocumentViewModel.cs
│       │   └── HealthMonitoringDocumentViewModel.cs
│       ├── Converters/
│       └── Program.cs
│
├── tests/
│   ├── TableSpec.Domain.Tests/
│   ├── TableSpec.Application.Tests/
│   └── TableSpec.Infrastructure.Tests/
│
└── docs/
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
1. 點擊「設定連線」按鈕
2. 點擊「新增」建立新連線
3. 輸入連線資訊（伺服器、資料庫、驗證方式）
4. 點擊「測試連線」確認連線成功
5. 點擊「儲存」保存設定
6. 點擊「連線」或關閉視窗後從下拉選單選擇連線

### 2. 瀏覽物件
- 左側樹狀結構顯示所有資料庫物件
- 使用搜尋框過濾物件
- 點擊物件查看詳細資訊

### 3. 查看詳細資訊
- **欄位** - 顯示欄位定義，可編輯說明
- **索引** - 顯示索引資訊（僅資料表）
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

### 6. 匯出 Excel
- 點擊「匯出 Excel」按鈕
- 選擇儲存位置
- 匯出包含所有物件規格的 Excel 檔案

### 7. 備份與還原
1. 按 Ctrl+B 或選單「工具 > 備份與還原」
2. **備份**：選擇連線、備份類型（完整/差異/交易記錄），設定儲存路徑後點擊「備份」
3. **還原**：選擇備份檔案，選擇覆蓋現有或建立新資料庫，點擊「還原」
4. **歷史記錄**：從歷史分頁可快速檢視過去的備份，並可直接還原

### 8. 健康監控
1. 選單「工具 > 健康監控」
2. 首次使用需要安裝監控系統（會在目標伺服器建立 DBA 資料庫）
3. 安裝完成後可檢視：
   - **總覽**：各監控類型的狀態摘要卡片
   - **即時指標**：所有監控指標的詳細資料
   - **告警**：最近的警告和危險狀態紀錄
   - **趨勢**：歷史趨勢圖表
   - **監控設定**：管理監控類別的啟用狀態和檢查間隔

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
