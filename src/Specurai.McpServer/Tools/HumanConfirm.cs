using System.Text.Json;
using ModelContextProtocol.Protocol;
using Specurai.Domain.Entities;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 向使用者本人索取確認的通道（可替換以便測試）
/// </summary>
public interface IConfirmPrompt
{
    /// <summary>client 是否支援 elicitation</summary>
    bool IsSupported { get; }

    /// <summary>送出確認表單並等待使用者回應</summary>
    Task<ElicitResult> ElicitAsync(ElicitRequestParams request, CancellationToken ct);
}

/// <summary>
/// 以 MCP elicitation 實作的確認通道
/// </summary>
internal sealed class McpServerConfirmPrompt(ModelContextProtocol.Server.McpServer server) : IConfirmPrompt
{
    public bool IsSupported => SupportsForm(server.ClientCapabilities?.Elicitation);

    /// <summary>
    /// client 是否支援表單模式 elicitation；依規格，未宣告任何模式（空物件）視為支援表單
    /// </summary>
    internal static bool SupportsForm(ElicitationCapability? capability) =>
        capability is { } c && (c.Form != null || c.Url == null);

    public async Task<ElicitResult> ElicitAsync(ElicitRequestParams request, CancellationToken ct) =>
        await server.ElicitAsync(request, ct);
}

/// <summary>
/// 破壞性操作執行前的人工確認閘門：模型帶 confirm:true 仍須使用者本人同意才放行
/// </summary>
public static class HumanConfirm
{
    private const string NameField = "confirmName";

    /// <summary>
    /// 向使用者確認；回傳 null 表示放行，否則為拒絕訊息（呼叫端應直接回傳、不執行）
    /// </summary>
    public static Task<string?> RequireAsync(ModelContextProtocol.Server.McpServer? server, string summary, string? expectedName,
        ConnectionProfile? target = null, CancellationToken ct = default) =>
        RequireAsync(server == null ? null : new McpServerConfirmPrompt(server), summary, expectedName, target, ct);

    /// <summary>
    /// 把模型提供的字串嵌入表單前去除控制與格式字元並加引號，避免以換行偽造表單說明
    /// </summary>
    public static string Quote(string? value) =>
        $"「{new string((value ?? "").Where(ch => !char.IsControl(ch) && char.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.Format).ToArray())}」";

    /// <summary>
    /// 同上，可注入確認通道；client 不支援 elicitation 時維持既有 confirm:true 行為（放行）
    /// </summary>
    public static async Task<string?> RequireAsync(IConfirmPrompt? prompt, string summary, string? expectedName,
        ConnectionProfile? target = null, CancellationToken ct = default)
    {
        if (prompt == null || !prompt.IsSupported)
            return null;

        // 確認目標不明時不能讓空白輸入放行
        if (expectedName != null && string.IsNullOrWhiteSpace(expectedName))
            return "無法判定確認目標，未執行。";

        var request = new ElicitRequestParams
        {
            Message = (target == null ? "" : $"連線：{Quote(target.Name)}（server {Quote(target.Server)}）\n")
                + (expectedName == null ? summary : $"{summary}\n\n請輸入{Quote(expectedName)}以確認。"),
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = expectedName == null
                    ? new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>()
                    : new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        [NameField] = new ElicitRequestParams.StringSchema { Title = "確認名稱" }
                    },
                Required = expectedName == null ? null : [NameField]
            }
        };

        ElicitResult result;
        try
        {
            result = await prompt.ElicitAsync(request, ct);
        }
        catch (Exception ex)
        {
            // 確認失敗一律不執行，不退回直接執行
            return $"無法取得使用者確認（{ex.Message}），未執行。";
        }

        if (!string.Equals(result.Action, "accept", StringComparison.OrdinalIgnoreCase))
            return "使用者拒絕執行，未做任何變更。";

        if (expectedName == null)
            return null;

        var typed = result.Content != null
            && result.Content.TryGetValue(NameField, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        return string.Equals(typed?.Trim(), expectedName.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : $"確認名稱不符（預期：{expectedName}），未執行。";
    }
}
