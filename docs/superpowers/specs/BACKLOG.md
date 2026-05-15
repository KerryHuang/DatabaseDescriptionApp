# Spec Backlog

待主題啟動時再個別開 spec。每項列出範圍、所屬功能、依據來源。

---

## 效能診斷擴充：實例健康快照

- **所屬功能**：效能診斷（`PerformanceDiagnosticsDocumentView`）
- **動機**：補齊 SQL Server 實例層級的一次性健康檢查，現有功能聚焦於查詢/索引/wait stats，缺少實例組態面。
- **範圍**：新增單一報表分頁「實例健康快照」，包含：
  - **VLF 數量**：查 `sys.dm_db_log_info`，每個 DB 的 VLF 數量 + 健康判斷（>1000 紅、500-1000 黃、<500 綠）
  - **TempDB 配置**：檔案數是否 = CPU 核心數（或 8，取小者）、各檔大小是否一致、TF1117/1118 狀態（2016+ 自動內建）
  - **Max Server Memory**：目前設定 vs OS 總記憶體 vs 建議值（總記憶體 - max(2GB, 10%)）
  - **各 DB 最後 CHECKDB 時間**：解析 `DBCC DBINFO` 的 `dbi_dbccLastKnownGood`，距今天數
- **不在範圍**：不主動修改任何設定，純檢查回報。
- **參考來源**：2026-05-15 對話「優先度表」P1 項目歸屬討論。

---

## 健康監控擴充：Wait Stats 長期 Baseline

- **所屬功能**：健康監控（`HealthMonitoringDocumentView`，已安裝的長期監控系統）
- **動機**：現有效能診斷只能看「自上次重啟以來」的 wait stats 累積值，無法判斷「相對於平日基準是否異常」。長期 baseline 是效能問題第一線判斷依據。
- **範圍**：
  - 在 `HealthMonitoringInstall.sql` 新增 `WaitStatsSnapshot` table（時間戳 + wait_type + 累積差量）
  - 新增排程每小時快照 `sys.dm_os_wait_stats`
  - 利用既有 `GetTrendDataAsync` 結構提供趨勢圖
  - UI 新增「Wait Stats 趨勢」分頁
- **參考來源**：2026-05-15 對話。

---

## 維護計劃擴充：索引維護排程

- **所屬功能**：維護計劃（`MaintenancePlanDocumentView`）
- **動機**：索引碎片化是線上效能慢性問題，需要週期性 rebuild/reorganize。
- **範圍**：
  - 新增 enum `CreateIndexMaintenanceJob`
  - 建立 SQL Agent Job，每週執行條件式維護：
    - 碎片 > 30% 且頁數 > 1000 → `REBUILD WITH (ONLINE = ON)`（Enterprise）/ `REBUILD`（Standard）
    - 碎片 5-30% 且頁數 > 1000 → `REORGANIZE`
    - 其餘忽略
  - Config 加 `IndexMaintenanceTime`、`IndexMaintenanceDayOfWeek`
- **參考來源**：2026-05-15 對話「優先度表」P1。

---

## 文件：DBA 維護策略最佳實務

- **所屬位置**：`docs/UserGuide.md` 或 `docs/MaintenanceBestPractices.md`
- **動機**：「備份多久一次？保留幾天？是否測試還原？」屬於人為決策，無法做成 SQL 檢查項目，但對使用者有引導價值。
- **範圍**：靜態文件，列出常見決策題與業界經驗值（OLTP / OLAP / 開發環境的不同建議）。
- **參考來源**：2026-05-15 對話「優先度表」P0 備份策略確認項。
