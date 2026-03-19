-- ==========================================
-- SQL Server 健康監控系統移除腳本
-- 版本: 3.0
-- ==========================================

SET NOCOUNT ON;
GO

-- ==========================================
-- 步驟 1: 移除 SQL Agent 作業
-- ==========================================
USE msdb;
GO

IF EXISTS (SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'DBA - Server Health Monitoring')
BEGIN
    EXEC msdb.dbo.sp_delete_job @job_name = N'DBA - Server Health Monitoring';
END
GO

IF EXISTS (SELECT job_id FROM msdb.dbo.sysjobs WHERE name = N'DBA - Cleanup Health Log')
BEGIN
    EXEC msdb.dbo.sp_delete_job @job_name = N'DBA - Cleanup Health Log';
END
GO

-- 如果只需要移除作業，到此為止
-- 以下步驟將移除資料庫物件

-- ==========================================
-- 步驟 2: 移除視圖
-- ==========================================
USE DBA;
GO

IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_RecentAlerts')
    DROP VIEW dbo.vw_RecentAlerts;
GO

IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_CurrentStatus')
    DROP VIEW dbo.vw_CurrentStatus;
GO

IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_TrendAnalysis')
    DROP VIEW dbo.vw_TrendAnalysis;
GO

-- ==========================================
-- 步驟 3: 移除預存程序
-- ==========================================
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MasterHealthCheck')
    DROP PROCEDURE dbo.sp_MasterHealthCheck;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_LogHealthMetrics')
    DROP PROCEDURE dbo.sp_LogHealthMetrics;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorCPU')
    DROP PROCEDURE dbo.sp_MonitorCPU;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorBlocking')
    DROP PROCEDURE dbo.sp_MonitorBlocking;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorLongQueries')
    DROP PROCEDURE dbo.sp_MonitorLongQueries;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorBackups')
    DROP PROCEDURE dbo.sp_MonitorBackups;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorDeadlocks')
    DROP PROCEDURE dbo.sp_MonitorDeadlocks;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorConnections')
    DROP PROCEDURE dbo.sp_MonitorConnections;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorTempDB')
    DROP PROCEDURE dbo.sp_MonitorTempDB;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorTransactionLog')
    DROP PROCEDURE dbo.sp_MonitorTransactionLog;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorSQLAgentJobs')
    DROP PROCEDURE dbo.sp_MonitorSQLAgentJobs;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_MonitorPerformance')
    DROP PROCEDURE dbo.sp_MonitorPerformance;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_CleanupHealthLog')
    DROP PROCEDURE dbo.sp_CleanupHealthLog;
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_HealthReport')
    DROP PROCEDURE dbo.sp_HealthReport;
GO

-- ==========================================
-- 步驟 4: 移除資料表（可選）
-- 如果要保留歷史資料，請註解以下區塊
-- ==========================================
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ServerHealthLog')
    DROP TABLE dbo.ServerHealthLog;
GO

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'MonitoringCategories')
    DROP TABLE dbo.MonitoringCategories;
GO

-- ==========================================
-- 步驟 5: 移除 DBA 資料庫（可選）
-- 警告：這將刪除整個 DBA 資料庫！
-- ==========================================
USE master;
GO

-- 如果 DBA 資料庫中沒有其他物件，可以刪除整個資料庫
-- 取消以下註解以啟用
/*
IF EXISTS (SELECT name FROM sys.databases WHERE name = 'DBA')
BEGIN
    ALTER DATABASE DBA SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE DBA;
END
GO
*/
