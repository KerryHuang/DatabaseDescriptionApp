using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 資料庫 Recovery Model MCP 工具
/// </summary>
[McpServerToolType]
public static class RecoveryModelTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool, Description("列出目前伺服器上所有資料庫的 Recovery Model")]
    public static async Task<string> ListRecoveryModels(IDatabaseRecoveryModelService service)
    {
        try
        {
            var list = (await service.GetAllAsync()).ToList();
            return JsonSerializer.Serialize(list, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"取得 Recovery Model 失敗：{ex.Message}";
        }
    }

    [McpServerTool, Description("設定指定資料庫的 Recovery Model（⚠️ 變更資料庫設定，會影響交易記錄行為）")]
    public static async Task<string> SetRecoveryModel(
        IDatabaseRecoveryModelService service,
        [Description("資料庫名稱")] string database,
        [Description("Recovery Model：FULL / SIMPLE / BULK_LOGGED")] string model)
    {
        try
        {
            var normalized = model.ToUpperInvariant().Replace("-", "_");
            if (normalized is not ("FULL" or "SIMPLE" or "BULK_LOGGED"))
                return "Model 必須為 FULL / SIMPLE / BULK_LOGGED。";

            await service.SaveChangesAsync(new[] { (database, normalized) });
            return $"已設定 [{database}] 的 Recovery Model = {normalized}。";
        }
        catch (Exception ex)
        {
            return $"設定 Recovery Model 失敗：{ex.Message}";
        }
    }
}
