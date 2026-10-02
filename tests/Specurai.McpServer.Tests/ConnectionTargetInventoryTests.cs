using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

/// <summary>
/// 單次呼叫連線參數的盤點守門：哪些工具支援 connection／database，以及宣告是否一致。
/// 新增依目前連線執行的工具時，必須加 [TargetConnection] 並更新這裡的清單。
/// </summary>
public class ConnectionTargetInventoryTests
{
    // 單庫工具：connection + database
    private static readonly string[] WithDatabase =
    [
        "ExecuteReadonlySql", "SearchColumns", "GetCreateTableSql", "DryRunSql", "ExecuteSql", "ExecuteDdl",
        "ListTables", "GetColumns", "GetIndexes", "GetRelations", "GetParameters", "GetDefinition",
        "GetTableStatistics", "GetExactRowCount", "GetColumnUsageStatistics",
        "UpdateTableDescription", "UpdateColumnDescription",
        "ExportAllToExcel", "ExportTableToExcel",
        "ScanUsage", "GetMissingIndexes", "GetUnusedIndexes",
        "GetHealthInstallStatus", "GetHealthStatus", "GetHealthMetrics", "GetHealthAlerts",
        "InstallHealthMonitoring", "UninstallHealthMonitoring"
    ];

    // server 層級、或工具本身已有資料庫參數：只收 connection
    private static readonly string[] ConnectionOnly =
    [
        "ListDatabases", "TestConnection",
        "ListAgentJobs", "ListNonSpecuraiJobs", "GetAgentJobHistory", "SetAgentJobEnabled",
        "StartAgentJob", "DeleteAgentJob", "UpdateAgentJobSchedule", "ImportAgentJob",
        "GetWaitStatistics", "GetExpensiveQueries", "GetExpensiveProcedures", "GetErrorLog",
        "BackupRun", "BackupVerify", "BackupInfo", "RestoreRun",
        "CheckMaintenancePrerequisites", "CheckMaintenanceSteps", "GenerateMaintenancePlanSql", "ExecuteMaintenancePlan",
        "ListRecoveryModels", "SetRecoveryModel"
    ];

    private static IEnumerable<MethodInfo> AllTools() =>
        typeof(SqlTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null);

    [Fact]
    public void 標記TargetConnection的工具_應與盤點清單一致()
    {
        var tagged = AllTools()
            .Select(m => (m.Name, Attr: m.GetCustomAttribute<TargetConnectionAttribute>()))
            .Where(x => x.Attr != null)
            .ToList();

        tagged.Where(x => x.Attr!.AcceptsDatabase).Select(x => x.Name)
            .Should().BeEquivalentTo(WithDatabase);
        tagged.Where(x => !x.Attr!.AcceptsDatabase).Select(x => x.Name)
            .Should().BeEquivalentTo(ConnectionOnly);
    }

    [Fact]
    public void 標記TargetConnection的工具_應宣告選填且描述一致的參數()
    {
        var problems = new List<string>();

        foreach (var method in AllTools())
        {
            var attr = method.GetCustomAttribute<TargetConnectionAttribute>();
            var parameters = method.GetParameters().ToDictionary(p => p.Name!);

            if (attr == null)
            {
                if (parameters.ContainsKey("connection"))
                    problems.Add($"{method.Name}：宣告了 connection 卻沒有 [TargetConnection]");
                continue;
            }

            Check(method, parameters, "connection", ConnectionTarget.ConnectionDescription, problems);
            if (attr.AcceptsDatabase)
                Check(method, parameters, "database", ConnectionTarget.DatabaseDescription, problems);
        }

        problems.Should().BeEmpty();
    }

    private static void Check(
        MethodInfo method, Dictionary<string, ParameterInfo> parameters, string name, string description,
        List<string> problems)
    {
        if (!parameters.TryGetValue(name, out var p))
        {
            problems.Add($"{method.Name}：缺少 {name} 參數");
            return;
        }

        if (p.ParameterType != typeof(string) || !p.HasDefaultValue || p.DefaultValue != null)
            problems.Add($"{method.Name}：{name} 應為 string? 且預設 null");
        if (p.GetCustomAttribute<DescriptionAttribute>()?.Description != description)
            problems.Add($"{method.Name}：{name} 描述應使用 ConnectionTarget 共用常數");
    }
}
