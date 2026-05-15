# Spec Backlog

待主題啟動時再個別開 spec。每項列出範圍、所屬功能、依據來源。

---

## ~~效能診斷擴充：實例健康快照~~ ✅ 已開 spec(進行中)

- **狀態**:2026-05-15 開 spec — `2026-05-15-performance-diagnostics-instance-health-design.md`
- **已完成項目(v1.14.0)**:各 DB 最後 CHECKDB 時間 → 已在「完整性檢查」分頁
- **本次 spec 涵蓋**:VLF 數量 / TempDB 配置 / Max Server Memory(三項剩餘)

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
