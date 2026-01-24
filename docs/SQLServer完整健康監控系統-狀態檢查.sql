-- 查看健康報表
EXEC DBA.dbo.sp_HealthReport;

-- 查看當前狀態
SELECT * FROM DBA.dbo.vw_CurrentStatus;

-- 查看最近告警
SELECT * FROM DBA.dbo.vw_RecentAlerts;

