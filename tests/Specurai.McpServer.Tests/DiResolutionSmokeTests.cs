using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Specurai.Infrastructure;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class DiResolutionSmokeTests
{
    private static bool HasAttribute(MemberInfo member, string attributeName) =>
        member.GetCustomAttributes(inherit: false)
            .Any(a => a.GetType().Name == attributeName);

    // 涵蓋邊界：僅檢查 [McpServerTool] 方法「直接參數」中的 Specurai.* 介面注入；
    // 不涵蓋經由 resolver/helper 間接取得的相依（目前所有注入皆為直接參數）。
    [Fact(DisplayName = "所有 MCP 工具注入的 Specurai 服務都應能由 AddSpecuraiCore 解析")]
    public void AllMcpToolInjectedServices_ShouldBeResolvable()
    {
        using var provider = new ServiceCollection().AddSpecuraiCore().BuildServiceProvider();
        var assembly = typeof(BackupTools).Assembly;
        var missing = new List<string>();
        var checkedCount = 0;

        var toolTypes = assembly.GetTypes()
            .Where(t => HasAttribute(t, "McpServerToolTypeAttribute"))
            .ToList();

        foreach (var type in toolTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => HasAttribute(m, "McpServerToolAttribute"));

            foreach (var method in methods)
            {
                foreach (var p in method.GetParameters())
                {
                    var pt = p.ParameterType;
                    if (pt.IsInterface && pt.Namespace?.StartsWith("Specurai") == true)
                    {
                        checkedCount++;
                        if (provider.GetService(pt) == null)
                            missing.Add($"{type.Name}.{method.Name}({pt.Name})");
                    }
                }
            }
        }

        // 防止反射抓不到工具導致假性通過
        toolTypes.Should().NotBeEmpty("應反射到至少一個 [McpServerToolType] 工具類別");
        checkedCount.Should().BeGreaterThan(0, "應檢查到至少一個注入的 Specurai 服務參數");

        missing.Should().BeEmpty(
            "以下 MCP 工具注入的服務無法解析：\n" + string.Join("\n", missing));
    }
}
