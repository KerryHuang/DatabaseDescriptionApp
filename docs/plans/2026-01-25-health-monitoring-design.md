# SQL Server 健康監控功能實作計畫

> 建立日期：2026-01-25
> 狀態：✅ 已完成
> 優先級：中

---

## 一、專案概述

### 1.1 目標

在現有 Specurai 專案中新增「SQL Server 健康監控」功能，提供資料庫健康狀態的視覺化監控和告警管理。

### 1.2 功能需求

| 功能 | 說明 |
|------|------|
| **安裝監控系統** | 自動建立 DBA 資料庫、監控資料表、預存程序、視圖和 SQL Agent 作業 |
| **移除監控系統** | 支援完整移除、保留歷史資料、只移除作業等多種模式 |
| **狀態總覽** | 顯示各類別（Memory、CPU、Disk 等）的健康狀態摘要 |
| **即時指標** | 顯示目前的各項健康指標數值 |
| **告警管理** | 顯示最近的告警記錄，支援天數篩選 |
| **趨勢圖表** | 使用 LiveCharts2 繪製指標趨勢變化圖 |
| **監控設定** | 管理監控類別的啟用狀態和檢查間隔 |

### 1.3 使用情境

1. **資料庫管理**：DBA 監控資料庫健康狀態
2. **效能調校**：透過趨勢圖表分析效能瓶頸
3. **預警機制**：及早發現潛在問題

---

## 二、架構設計

### 2.1 整體架構

```
┌─────────────────────────────────────────────────────────────────┐
│                        Desktop 層 (MDI 架構)                     │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  MainWindow.axaml (TabControl 容器)                      │   │
│  │  └── HealthMonitoringDocumentView.axaml (UserControl)    │   │
│  │       └── HealthMonitoringDocumentViewModel              │   │
│  │            ├── 安裝/移除管理                              │   │
│  │            ├── 狀態總覽、即時指標                         │   │
│  │            ├── 告警管理                                   │   │
│  │            ├── 趨勢圖表 (LiveCharts2)                     │   │
│  │            └── 監控設定                                   │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Application 層                              │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  IHealthMonitoringService                                │   │
│  │  ├── GetInstallStatusAsync()                            │   │
│  │  ├── InstallAsync(progress)                             │   │
│  │  ├── UninstallAsync(options, progress)                  │   │
│  │  ├── GetStatusSummaryAsync()                            │   │
│  │  ├── GetCurrentMetricsAsync()                           │   │
│  │  ├── GetRecentAlertsAsync(days)                         │   │
│  │  ├── GetTrendDataAsync(checkType, metricName, days)     │   │
│  │  ├── ExecuteHealthCheckAsync()                          │   │
│  │  ├── GetCategoriesAsync()                               │   │
│  │  └── UpdateCategoryAsync(categoryId, isEnabled, interval)│   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  IHealthMonitoringInstaller                              │   │
│  │  ├── InstallAsync(progress)                             │   │
│  │  └── UninstallAsync(options, progress)                  │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Infrastructure 層                            │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  HealthMonitoringRepository                              │   │
│  │  └── 實作 IHealthMonitoringRepository                    │   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  HealthMonitoringInstaller                               │   │
│  │  └── 讀取嵌入式 SQL 腳本並執行                            │   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Embedded Resources (Scripts/)                           │   │
│  │  ├── HealthMonitoringInstall.sql                        │   │
│  │  └── HealthMonitoringUninstall.sql                      │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        Domain 層                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Entities                                                │   │
│  │  ├── HealthLogEntry          # 健康記錄                  │   │
│  │  ├── MonitoringCategory      # 監控類別                  │   │
│  │  ├── HealthStatusSummary     # 狀態摘要                  │   │
│  │  ├── HealthMetric            # 即時指標                  │   │
│  │  ├── TrendDataPoint          # 趨勢資料點                │   │
│  │  └── HealthMonitoringInstallStatus  # 安裝狀態           │   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Interfaces                                              │   │
│  │  └── IHealthMonitoringRepository                        │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 資料流程

```
安裝流程:
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│ 檢查狀態     │ → │ 讀取腳本     │ → │ 分批執行     │ → │ 驗證安裝     │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘

監控資料流程:
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│ SQL Agent   │ → │ 執行健康檢查 │ → │ 寫入記錄     │ → │ UI 顯示     │
│ 定時觸發    │    │ 預存程序     │    │ HealthLog    │    │ 狀態/圖表   │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘

移除流程:
┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│ 選擇模式     │ → │ 執行移除腳本 │ → │ 驗證移除     │
│ (三種選項)   │    │             │    │             │
└─────────────┘    └─────────────┘    └─────────────┘
```

---

## 三、Domain 層設計

### 3.1 新增檔案清單

```
src/Specurai.Domain/
├── Entities/
│   ├── HealthLogEntry.cs
│   ├── MonitoringCategory.cs
│   ├── HealthStatusSummary.cs
│   ├── HealthMetric.cs
│   ├── TrendDataPoint.cs
│   └── HealthMonitoringInstallStatus.cs
└── Interfaces/
    └── IHealthMonitoringRepository.cs
```

### 3.2 實體設計

#### HealthLogEntry.cs

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 健康記錄實體
/// </summary>
public class HealthLogEntry
{
    /// <summary>記錄 ID</summary>
    public required int LogId { get; init; }

    /// <summary>檢查時間</summary>
    public required DateTime CheckTime { get; init; }

    /// <summary>檢查類型 (Memory, CPU, Disk, etc.)</summary>
    public required string CheckType { get; init; }

    /// <summary>指標名稱</summary>
    public required string MetricName { get; init; }

    /// <summary>指標值</summary>
    public decimal? MetricValue { get; init; }

    /// <summary>閾值</summary>
    public decimal? ThresholdValue { get; init; }

    /// <summary>狀態 (OK, WARNING, CRITICAL)</summary>
    public string? Status { get; init; }

    /// <summary>告警訊息</summary>
    public string? AlertMessage { get; init; }

    /// <summary>伺服器名稱</summary>
    public string? ServerName { get; init; }

    /// <summary>資料庫名稱</summary>
    public string? DatabaseName { get; init; }

    /// <summary>附加資訊</summary>
    public string? AdditionalInfo { get; init; }
}
```

#### MonitoringCategory.cs

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 監控類別實體
/// </summary>
public class MonitoringCategory
{
    /// <summary>類別 ID</summary>
    public required int CategoryId { get; init; }

    /// <summary>類別名稱</summary>
    public required string CategoryName { get; init; }

    /// <summary>說明</summary>
    public string? Description { get; init; }

    /// <summary>是否啟用</summary>
    public bool IsEnabled { get; set; }

    /// <summary>檢查間隔 (分鐘)</summary>
    public int CheckIntervalMinutes { get; set; }

    /// <summary>上次檢查時間</summary>
    public DateTime? LastCheckTime { get; init; }

    /// <summary>目前狀態</summary>
    public string? CurrentStatus { get; init; }
}
```

#### HealthStatusSummary.cs

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 健康狀態摘要實體
/// </summary>
public class HealthStatusSummary
{
    /// <summary>檢查類型</summary>
    public required string CheckType { get; init; }

    /// <summary>整體狀態 (OK, WARNING, CRITICAL)</summary>
    public required string OverallStatus { get; init; }

    /// <summary>檢查項目總數</summary>
    public int TotalChecks { get; init; }

    /// <summary>OK 項目數</summary>
    public int OkCount { get; init; }

    /// <summary>WARNING 項目數</summary>
    public int WarningCount { get; init; }

    /// <summary>CRITICAL 項目數</summary>
    public int CriticalCount { get; init; }

    /// <summary>最後檢查時間</summary>
    public DateTime? LastCheckTime { get; init; }
}
```

#### HealthMetric.cs

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 即時健康指標實體
/// </summary>
public class HealthMetric
{
    /// <summary>檢查類型</summary>
    public required string CheckType { get; init; }

    /// <summary>指標名稱</summary>
    public required string MetricName { get; init; }

    /// <summary>目前值</summary>
    public decimal? CurrentValue { get; init; }

    /// <summary>閾值</summary>
    public decimal? ThresholdValue { get; init; }

    /// <summary>單位</summary>
    public string? Unit { get; init; }

    /// <summary>狀態</summary>
    public string? Status { get; init; }

    /// <summary>最後更新時間</summary>
    public DateTime? LastUpdated { get; init; }
}
```

#### TrendDataPoint.cs

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 趨勢資料點實體
/// </summary>
public class TrendDataPoint
{
    /// <summary>檢查時間</summary>
    public required DateTime CheckTime { get; init; }

    /// <summary>指標值</summary>
    public decimal? MetricValue { get; init; }

    /// <summary>閾值</summary>
    public decimal? ThresholdValue { get; init; }
}
```

#### HealthMonitoringInstallStatus.cs

```csharp
namespace Specurai.Domain.Entities;

/// <summary>
/// 健康監控安裝狀態實體
/// </summary>
public class HealthMonitoringInstallStatus
{
    /// <summary>DBA 資料庫是否存在</summary>
    public required bool DatabaseExists { get; init; }

    /// <summary>HealthLog 資料表是否存在</summary>
    public required bool HealthLogTableExists { get; init; }

    /// <summary>Categories 資料表是否存在</summary>
    public required bool CategoriesTableExists { get; init; }

    /// <summary>主要預存程序是否存在</summary>
    public required bool MasterProcedureExists { get; init; }

    /// <summary>視圖是否存在</summary>
    public required bool ViewsExist { get; init; }

    /// <summary>SQL Agent 作業是否存在</summary>
    public required bool AgentJobsExist { get; init; }

    /// <summary>記錄數量</summary>
    public int LogCount { get; init; }

    /// <summary>是否完整安裝</summary>
    public bool IsFullyInstalled => DatabaseExists && HealthLogTableExists &&
        CategoriesTableExists && MasterProcedureExists && ViewsExist;

    /// <summary>是否部分安裝</summary>
    public bool IsPartiallyInstalled => DatabaseExists || HealthLogTableExists ||
        CategoriesTableExists || MasterProcedureExists || ViewsExist;
}
```

### 3.3 Repository 介面

#### IHealthMonitoringRepository.cs

```csharp
namespace Specurai.Domain.Interfaces;

/// <summary>
/// 健康監控資料存取介面
/// </summary>
public interface IHealthMonitoringRepository
{
    /// <summary>取得安裝狀態</summary>
    Task<HealthMonitoringInstallStatus> GetInstallStatusAsync(CancellationToken ct = default);

    /// <summary>取得狀態摘要</summary>
    Task<IReadOnlyList<HealthStatusSummary>> GetStatusSummaryAsync(CancellationToken ct = default);

    /// <summary>取得目前指標</summary>
    Task<IReadOnlyList<HealthMetric>> GetCurrentMetricsAsync(CancellationToken ct = default);

    /// <summary>取得最近告警</summary>
    Task<IReadOnlyList<HealthLogEntry>> GetRecentAlertsAsync(int days = 7, CancellationToken ct = default);

    /// <summary>取得趨勢資料</summary>
    Task<IReadOnlyList<TrendDataPoint>> GetTrendDataAsync(
        string checkType, string metricName, int days = 30, CancellationToken ct = default);

    /// <summary>取得監控類別</summary>
    Task<IReadOnlyList<MonitoringCategory>> GetCategoriesAsync(CancellationToken ct = default);

    /// <summary>更新監控類別</summary>
    Task UpdateCategoryAsync(int categoryId, bool isEnabled, int checkIntervalMinutes, CancellationToken ct = default);

    /// <summary>執行健康檢查</summary>
    Task ExecuteHealthCheckAsync(CancellationToken ct = default);
}
```

---

## 四、Application 層設計

### 4.1 新增檔案清單

```
src/Specurai.Application/
└── Services/
    ├── IHealthMonitoringService.cs
    ├── IHealthMonitoringInstaller.cs
    └── HealthMonitoringService.cs
```

### 4.2 服務介面

#### IHealthMonitoringService.cs

```csharp
namespace Specurai.Application.Services;

/// <summary>
/// 健康監控服務介面
/// </summary>
public interface IHealthMonitoringService
{
    Task<HealthMonitoringInstallStatus> GetInstallStatusAsync(CancellationToken ct = default);
    Task<InstallResult> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
    Task<UninstallResult> UninstallAsync(UninstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
    Task<IReadOnlyList<HealthStatusSummary>> GetStatusSummaryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HealthMetric>> GetCurrentMetricsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HealthLogEntry>> GetRecentAlertsAsync(int days = 7, CancellationToken ct = default);
    Task<IReadOnlyList<TrendDataPoint>> GetTrendDataAsync(string checkType, string metricName, int days = 30, CancellationToken ct = default);
    Task ExecuteHealthCheckAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MonitoringCategory>> GetCategoriesAsync(CancellationToken ct = default);
    Task UpdateCategoryAsync(int categoryId, bool isEnabled, int checkIntervalMinutes, CancellationToken ct = default);
}

/// <summary>安裝進度</summary>
public record InstallProgress(int PercentComplete, string Message);

/// <summary>安裝結果</summary>
public record InstallResult(bool Success, string? ErrorMessage = null);

/// <summary>移除選項</summary>
public record UninstallOptions(bool KeepHistoryData = false, bool RemoveJobsOnly = false);

/// <summary>移除結果</summary>
public record UninstallResult(bool Success, string? ErrorMessage = null);
```

#### IHealthMonitoringInstaller.cs

```csharp
namespace Specurai.Application.Services;

/// <summary>
/// 健康監控安裝器介面
/// </summary>
public interface IHealthMonitoringInstaller
{
    Task<InstallResult> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
    Task<UninstallResult> UninstallAsync(UninstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
}
```

---

## 五、Infrastructure 層設計

### 5.1 新增檔案清單

```
src/Specurai.Infrastructure/
├── Repositories/
│   └── HealthMonitoringRepository.cs
├── Services/
│   └── HealthMonitoringInstaller.cs
└── Scripts/
    ├── HealthMonitoringInstall.sql   (嵌入資源)
    └── HealthMonitoringUninstall.sql (嵌入資源)
```

### 5.2 嵌入資源設定

在 `Specurai.Infrastructure.csproj` 中：

```xml
<ItemGroup>
  <EmbeddedResource Include="Scripts\*.sql" />
</ItemGroup>
```

### 5.3 HealthMonitoringRepository 實作重點

- 使用 `Func<string?>` 連線字串工廠模式
- 檢查 DBA 資料庫存在後再查詢視圖
- 使用 Dapper 查詢
- 處理資料庫不存在的情況

### 5.4 HealthMonitoringInstaller 實作重點

- 從嵌入資源讀取 SQL 腳本
- 以 `GO` 分割腳本批次執行
- 支援進度回報和取消
- 處理 SQL Agent 作業安裝（需要 msdb 權限）

### 5.5 SQL 安裝腳本內容

安裝腳本建立以下物件：

| 物件類型 | 物件名稱 | 說明 |
|---------|---------|------|
| 資料庫 | DBA | 健康監控專用資料庫 |
| 資料表 | HealthLog | 健康記錄 |
| 資料表 | MonitoringCategories | 監控類別 |
| 資料表 | MonitoringThresholds | 監控閾值 |
| 預存程序 | usp_HealthCheck_Master | 主要健康檢查程序 |
| 預存程序 | usp_HealthCheck_Memory | 記憶體檢查 |
| 預存程序 | usp_HealthCheck_CPU | CPU 檢查 |
| 預存程序 | usp_HealthCheck_Disk | 磁碟空間檢查 |
| 預存程序 | usp_HealthCheck_Connections | 連線數檢查 |
| 預存程序 | usp_HealthCheck_Blocking | 封鎖檢查 |
| 預存程序 | usp_HealthCheck_Deadlocks | 死結檢查 |
| 預存程序 | usp_HealthCheck_TempDB | TempDB 檢查 |
| 預存程序 | usp_HealthCheck_Backups | 備份檢查 |
| 預存程序 | usp_HealthCheck_Jobs | Agent 作業檢查 |
| 視圖 | vw_HealthStatusSummary | 狀態摘要視圖 |
| 視圖 | vw_CurrentMetrics | 目前指標視圖 |
| 視圖 | vw_RecentAlerts | 最近告警視圖 |
| SQL Agent 作業 | DBA_HealthCheck | 定時執行健康檢查 |

### 5.6 移除腳本支援三種模式

1. **完整移除**：刪除 SQL Agent 作業 → 預存程序/視圖 → 資料表 → DBA 資料庫
2. **保留歷史資料**：只刪除 SQL Agent 作業、預存程序、視圖
3. **只移除作業**：只刪除 SQL Agent 作業

---

## 六、Desktop 層設計

### 6.1 新增檔案清單

```
src/Specurai.Desktop/
├── ViewModels/
│   └── HealthMonitoringDocumentViewModel.cs
├── Views/
│   ├── HealthMonitoringDocumentView.axaml
│   └── HealthMonitoringDocumentView.axaml.cs
└── Converters/
    └── HealthMonitoringConverters.cs
```

### 6.2 套件相依

在 `Specurai.Desktop.csproj` 中新增：

```xml
<PackageReference Include="LiveChartsCore.SkiaSharpView.Avalonia" Version="2.0.0-rc2" />
```

### 6.3 UI 設計

```
┌───────────────────────────────────────────────────────────────────────────┐
│ [工具列]                                                                   │
│  [設定] [看板] [刷新] [執行健康檢查] [取消]                                  │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ═══════════════════════ 設定面板 ═══════════════════════                 │
│                                                                           │
│  [安裝狀態]                                                                │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ DBA 資料庫: ✅ 已存在    HealthLog 資料表: ✅ 已存在                  │ │
│  │ 預存程序: ✅ 已存在      視圖: ✅ 已存在                              │ │
│  │ SQL Agent 作業: ✅ 已存在  記錄數: 1,234                             │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
│  [安裝/移除]                                                               │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ [安裝健康監控系統]                                                    │ │
│  │                                                                     │ │
│  │ 移除選項:                                                            │ │
│  │ [ ] 保留歷史資料    [ ] 只移除 SQL Agent 作業                         │ │
│  │ [移除健康監控系統]                                                    │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
│  [進度]                                                                    │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ ████████████████████████████░░░░░░░░░░  75%                         │ │
│  │ 正在建立預存程序...                                                  │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ═══════════════════════ 看板面板 ═══════════════════════                 │
│                                                                           │
│  [分頁: 總覽 | 即時指標 | 告警 | 趨勢 | 監控設定]                          │
│                                                                           │
│  ───────────────────── 總覽分頁 ─────────────────────                     │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐            │
│  │ Memory  │ │  CPU    │ │  Disk   │ │ Connect │ │ TempDB  │            │
│  │   ✅    │ │   ⚠    │ │   ✅    │ │   ✅    │ │   ✅    │            │
│  │   OK    │ │ WARNING │ │   OK    │ │   OK    │ │   OK    │            │
│  └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────────┘            │
│                                                                           │
│  ───────────────────── 趨勢分頁 ─────────────────────                     │
│  類型: [Memory ▼]  指標: [Memory Usage % ▼]                              │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │                                                                     │ │
│  │   100% ─┤                                                           │ │
│  │         │     ╱╲                                                    │ │
│  │    80% ─┤    ╱  ╲    ╱╲                                            │ │
│  │         │   ╱    ╲  ╱  ╲                                            │ │
│  │    60% ─┤  ╱      ╲╱    ╲                                           │ │
│  │         │ ╱              ╲                  (LiveCharts2)           │ │
│  │    40% ─┤╱                ╲─────────────────                        │ │
│  │         │                                                           │ │
│  │    20% ─┤                                                           │ │
│  │         └────────────────────────────────────────────────────────── │ │
│  │          01/20    01/21    01/22    01/23    01/24    01/25         │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
├───────────────────────────────────────────────────────────────────────────┤
│ [狀態列] 已安裝健康監控系統                                                 │
└───────────────────────────────────────────────────────────────────────────┘
```

### 6.4 HealthMonitoringDocumentViewModel 設計

```csharp
/// <summary>
/// 健康監控文件 ViewModel（MDI Document）
/// </summary>
public partial class HealthMonitoringDocumentViewModel : DocumentViewModel
{
    private readonly IHealthMonitoringService? _healthMonitoringService;
    private readonly IConnectionManager? _connectionManager;
    private CancellationTokenSource? _cancellationTokenSource;

    public override string DocumentType => "HealthMonitoring";
    public override string DocumentKey => DocumentType; // 只允許開啟一個實例

    #region 安裝狀態

    [ObservableProperty]
    private HealthMonitoringInstallStatus? _installStatus;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _showSetupPanel = true;

    [ObservableProperty]
    private bool _showDashboard;

    #endregion

    #region 移除選項

    [ObservableProperty]
    private bool _keepHistoryData;

    [ObservableProperty]
    private bool _removeJobsOnly;

    #endregion

    #region 處理狀態

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteHealthCheckCommand))]
    private bool _isProcessing;

    [ObservableProperty]
    private int _progressPercentage;

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "就緒";

    #endregion

    #region 資料集合

    public ObservableCollection<HealthStatusSummary> StatusSummaries { get; } = [];
    public ObservableCollection<HealthMetric> CurrentMetrics { get; } = [];
    public ObservableCollection<HealthLogEntry> RecentAlerts { get; } = [];
    public ObservableCollection<MonitoringCategory> Categories { get; } = [];

    #endregion

    #region 趨勢圖表 (LiveCharts2)

    [ObservableProperty]
    private string _selectedTrendCheckType = "Memory";

    [ObservableProperty]
    private string _selectedTrendMetricName = "Memory Usage %";

    public ObservableCollection<ISeries> TrendSeries { get; } = [];
    public Axis[] TrendXAxes { get; }
    public Axis[] TrendYAxes { get; }

    #endregion

    // 命令
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync() { /* ... */ }

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync() { /* ... */ }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync() { /* ... */ }

    [RelayCommand(CanExecute = nameof(CanExecuteHealthCheck))]
    private async Task ExecuteHealthCheckAsync() { /* ... */ }

    [RelayCommand]
    private void CancelOperation() { /* ... */ }
}
```

### 6.5 轉換器

HealthMonitoringConverters.cs 包含：

| 轉換器 | 說明 |
|--------|------|
| HealthStatusColorConverter | OK=綠, WARNING=橘, CRITICAL=紅 |
| HealthStatusIconConverter | OK=✓, WARNING=⚠, CRITICAL=✗ |
| OverallStatusBackgroundConverter | 狀態對應的半透明背景色 |
| InstallStatusColorConverter | 安裝狀態顏色 |

---

## 七、MainWindow 整合

### 7.1 新增選單項目

```xml
<MenuItem Header="工具(_T)">
    <!-- 其他選單項目 -->
    <MenuItem Header="健康監控(_H)" Command="{Binding OpenHealthMonitoringCommand}"
              InputGesture="Ctrl+H" IsEnabled="{Binding IsConnected}">
        <MenuItem.Icon>
            <TextBlock Text="🩺" FontSize="14"/>
        </MenuItem.Icon>
    </MenuItem>
</MenuItem>
```

### 7.2 MainWindowViewModel 新增命令

```csharp
[RelayCommand]
private void OpenHealthMonitoring()
{
    // 檢查是否已開啟
    var existing = Documents.OfType<HealthMonitoringDocumentViewModel>().FirstOrDefault();
    if (existing != null)
    {
        SelectedDocument = existing;
        return;
    }

    var doc = App.Services?.GetRequiredService<HealthMonitoringDocumentViewModel>()
        ?? new HealthMonitoringDocumentViewModel();
    doc.CloseRequested += OnDocumentCloseRequested;
    Documents.Add(doc);
    SelectedDocument = doc;
}
```

### 7.3 新增 DataTemplate

```xml
<DataTemplate DataType="{x:Type vm:HealthMonitoringDocumentViewModel}">
    <views:HealthMonitoringDocumentView/>
</DataTemplate>
```

### 7.4 DI 註冊

在 `Program.cs` 的 `ConfigureServices()` 中：

```csharp
// Infrastructure - Health Monitoring
services.AddSingleton<IHealthMonitoringRepository>(sp =>
    new HealthMonitoringRepository(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));
services.AddSingleton<IHealthMonitoringInstaller>(sp =>
    new HealthMonitoringInstaller(() => sp.GetRequiredService<IConnectionManager>().GetCurrentConnectionString()));

// Application - Health Monitoring Service
services.AddSingleton<IHealthMonitoringService, HealthMonitoringService>();

// ViewModel
services.AddTransient<HealthMonitoringDocumentViewModel>(sp =>
    new HealthMonitoringDocumentViewModel(
        sp.GetRequiredService<IHealthMonitoringService>(),
        sp.GetRequiredService<IConnectionManager>()));
```

---

## 八、實作步驟

### 階段 1：Domain 層 ✅

| 步驟 | 工作內容 | 狀態 |
|------|---------|------|
| 1.1 | 建立 HealthLogEntry 實體 | ✅ |
| 1.2 | 建立 MonitoringCategory 實體 | ✅ |
| 1.3 | 建立 HealthStatusSummary 實體 | ✅ |
| 1.4 | 建立 HealthMetric 實體 | ✅ |
| 1.5 | 建立 TrendDataPoint 實體 | ✅ |
| 1.6 | 建立 HealthMonitoringInstallStatus 實體 | ✅ |
| 1.7 | 建立 IHealthMonitoringRepository 介面 | ✅ |

### 階段 2：Application 層 ✅

| 步驟 | 工作內容 | 狀態 |
|------|---------|------|
| 2.1 | 建立 IHealthMonitoringService 介面 | ✅ |
| 2.2 | 建立 IHealthMonitoringInstaller 介面 | ✅ |
| 2.3 | 實作 HealthMonitoringService | ✅ |

### 階段 3：Infrastructure 層 ✅

| 步驟 | 工作內容 | 狀態 |
|------|---------|------|
| 3.1 | 實作 HealthMonitoringRepository | ✅ |
| 3.2 | 實作 HealthMonitoringInstaller | ✅ |
| 3.3 | 建立 HealthMonitoringInstall.sql 腳本 | ✅ |
| 3.4 | 建立 HealthMonitoringUninstall.sql 腳本 | ✅ |
| 3.5 | 設定嵌入資源 | ✅ |

### 階段 4：Desktop 層 ✅

| 步驟 | 工作內容 | 狀態 |
|------|---------|------|
| 4.1 | 建立 HealthMonitoringDocumentView.axaml | ✅ |
| 4.2 | 實作 HealthMonitoringDocumentViewModel | ✅ |
| 4.3 | 建立 HealthMonitoringConverters | ✅ |
| 4.4 | 新增 LiveCharts2 套件 | ✅ |
| 4.5 | 整合到 MainWindow | ✅ |
| 4.6 | DI 註冊 | ✅ |

### 階段 5：測試 ✅

| 步驟 | 工作內容 | 狀態 |
|------|---------|------|
| 5.1 | 建置成功 | ✅ |
| 5.2 | 所有測試通過 (459 個) | ✅ |

---

## 九、參考資料

- `src/Specurai.Desktop/ViewModels/BackupRestoreDocumentViewModel.cs` - DocumentViewModel 完整模式
- `src/Specurai.Infrastructure/Repositories/TableRepository.cs` - Repository 模式
- `src/Specurai.Desktop/Program.cs` - DI 註冊
- `src/Specurai.Desktop/Views/MainWindow.axaml` - MDI 整合
- `docs/SQLServer完整健康監控系統-統一安裝腳本.sql` - 安裝腳本參考

---

*文件建立日期：2026-01-25*
*實作完成日期：2026-01-25*
