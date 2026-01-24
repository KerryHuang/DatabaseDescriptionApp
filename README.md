# TableSpec - 資料庫規格查詢工具

TableSpec 是一個跨平台桌面應用程式，用於查詢和匯出 SQL Server 資料庫的結構規格，包含資料表、檢視表、預存程序和函數的詳細資訊。

## 功能特色

- **多連線管理** - 支援儲存多組資料庫連線設定，可快速切換
- **物件瀏覽** - 樹狀結構顯示 Tables、Views、Stored Procedures、Functions
- **搜尋過濾** - 即時搜尋物件名稱和說明
- **詳細資訊檢視**
  - 欄位資訊（名稱、型別、長度、主鍵、可空、預設值、說明）
  - 索引資訊（名稱、類型、唯一性、欄位）
  - 關聯資訊（外鍵約束、來源/目標表欄位）
  - 參數資訊（預存程序/函數的參數）
  - 定義（預存程序/函數的 SQL 程式碼）
- **Excel 匯出** - 將資料庫規格匯出為 Excel 檔案
- **跨平台** - 支援 Windows、macOS、Linux

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
| 主題樣式 | Fluent Theme |

## 專案結構

```
DatabaseDescriptionApp/
├── src/
│   ├── TableSpec.Domain/          # 領域層：實體、介面
│   │   ├── Entities/
│   │   │   ├── TableInfo.cs
│   │   │   ├── ColumnInfo.cs
│   │   │   ├── IndexInfo.cs
│   │   │   ├── RelationInfo.cs
│   │   │   ├── ParameterInfo.cs
│   │   │   └── ConnectionProfile.cs
│   │   └── Interfaces/
│   │       ├── ITableRepository.cs
│   │       ├── IColumnRepository.cs
│   │       ├── IIndexRepository.cs
│   │       ├── IRelationRepository.cs
│   │       └── IParameterRepository.cs
│   │
│   ├── TableSpec.Application/     # 應用層：服務介面與實作
│   │   └── Services/
│   │       ├── ITableQueryService.cs
│   │       ├── TableQueryService.cs
│   │       ├── IConnectionManager.cs
│   │       └── IExportService.cs
│   │
│   ├── TableSpec.Infrastructure/  # 基礎設施層：資料存取實作
│   │   ├── Repositories/
│   │   │   ├── TableRepository.cs
│   │   │   ├── ColumnRepository.cs
│   │   │   ├── IndexRepository.cs
│   │   │   ├── RelationRepository.cs
│   │   │   └── ParameterRepository.cs
│   │   └── Services/
│   │       ├── ConnectionManager.cs
│   │       └── ExcelExportService.cs
│   │
│   └── TableSpec.Desktop/         # 桌面應用層：UI
│       ├── Views/
│       │   ├── MainWindow.axaml
│       │   └── ConnectionSetupWindow.axaml
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs
│       │   ├── ObjectTreeViewModel.cs
│       │   ├── TableDetailViewModel.cs
│       │   └── ConnectionSetupViewModel.cs
│       ├── Converters/
│       └── Program.cs
│
├── tests/
│   ├── TableSpec.Domain.Tests/
│   ├── TableSpec.Application.Tests/
│   └── TableSpec.Infrastructure.Tests/
│
└── docs/
    └── plans/
```

## 系統需求

- .NET 8.0 SDK 或更高版本
- SQL Server 2012 或更高版本（支援 Windows 驗證或 SQL Server 驗證）

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

**macOS:**
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
- **欄位** - 顯示欄位定義（資料表/檢視表）
- **索引** - 顯示索引資訊（僅資料表）
- **關聯** - 顯示外鍵關聯（僅資料表）
- **參數** - 顯示參數定義（預存程序/函數）
- **定義** - 顯示 SQL 程式碼（預存程序/函數）

### 4. 匯出 Excel
- 點擊「匯出 Excel」按鈕
- 選擇儲存位置
- 匯出包含所有物件規格的 Excel 檔案

## 連線設定儲存位置

連線設定儲存於使用者 AppData 目錄：
- **Windows:** `%APPDATA%\TableSpec\connections.json`
- **macOS:** `~/.config/TableSpec/connections.json`
- **Linux:** `~/.config/TableSpec/connections.json`

## 授權條款

MIT License

## 貢獻

歡迎提交 Issue 和 Pull Request。
