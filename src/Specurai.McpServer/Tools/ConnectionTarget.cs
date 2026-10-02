using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Specurai.Application.Services;

namespace Specurai.McpServer.Tools;

/// <summary>
/// 標記工具支援單次呼叫連線參數：方法須宣告 <c>connection</c>（與 <c>database</c>，除非
/// <see cref="AcceptsDatabase"/> 為 false）參數，實際解析與套用由 <see cref="ConnectionTarget"/> filter 處理。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class TargetConnectionAttribute : Attribute
{
    /// <summary>是否接受 database 參數（工具既有參數已叫 database 或資料庫無意義時設為 false）</summary>
    public bool AcceptsDatabase { get; init; } = true;
}

/// <summary>
/// 單次呼叫連線參數：讓工具在單次呼叫指定連線與資料庫，不讀也不改 process 層的目前連線，
/// 避免同一個 server process 內的平行呼叫互相切走連線。
/// </summary>
internal static class ConnectionTarget
{
    public const string ConnectionDescription =
        "（選填）本次呼叫使用的連線名稱或 ID，解析規則同 switch_connection。指定時不讀也不改目前連線；" +
        "找不到、同名歧義、已停用都回錯誤，不會退回目前連線。有指定 connection 或 database 時，回應第一段附 executedOn";

    public const string DatabaseDescription =
        "（選填）本次呼叫使用的資料庫；未指定時用連線的預設資料庫。只帶 database 不帶 connection 時套用在目前連線的 server 上，" +
        "不改目前資料庫";

    private const string ExecutedOnPrefix = "executedOn:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 註冊單次呼叫連線參數的 CallTool filter
    /// </summary>
    public static IMcpServerBuilder AddConnectionTargetFilter(this IMcpServerBuilder builder) =>
        builder.AddCallToolFilter(next => (context, ct) =>
        {
            var tool = context.MatchedPrimitive as McpServerTool;
            var target = tool?.Metadata.OfType<TargetConnectionAttribute>().FirstOrDefault();
            if (tool == null || target == null)
                return next(context, ct);

            var cm = context.Services!.GetRequiredService<IConnectionManager>();
            return InvokeAsync(cm, tool, target, context.Params?.Arguments, () => next(context, ct));
        });

    private static async ValueTask<CallToolResult> InvokeAsync(
        IConnectionManager cm,
        McpServerTool tool,
        TargetConnectionAttribute target,
        IDictionary<string, JsonElement>? arguments,
        Func<ValueTask<CallToolResult>> next)
    {
        if (!TryReadString(arguments, "connection", out var connection))
            return Error("connection 參數必須是非空字串（連線名稱或 ID）；不指定請省略此參數。");

        string? database = null;
        if (target.AcceptsDatabase)
        {
            if (!TryReadString(arguments, "database", out database))
                return Error("database 參數必須是非空字串；不指定請省略此參數。");
        }
        else if (arguments?.ContainsKey("database") == true && !DeclaresParameter(tool, "database"))
        {
            // 靜默忽略會讓呼叫端以為作用在指定庫，等同跑錯庫，所以直接拒絕
            return Error("此工具不支援 database 參數（作用於 server 層級或連線預設資料庫）；請只指定 connection。");
        }

        if (connection == null && database == null)
            return await next();

        var profile = connection != null
            ? ProfileResolver.Resolve(cm, connection)
            : cm.GetCurrentProfile();
        if (profile == null)
        {
            return Error(connection != null
                ? ProfileResolver.DescribeMissing(cm, connection)
                : "未設定目前連線，無法只套用 database；請同時指定 connection。");
        }

        CallToolResult result;
        using (cm.BeginCallScope(profile, database))
        {
            result = await next();
        }

        // 只收 connection 的工具不附 database：實際作用的庫可能是工具自己的參數，附上反而誤導稽核
        var executedOn = target.AcceptsDatabase
            ? JsonSerializer.Serialize(new
            {
                connection = profile.Name,
                server = profile.Server,
                database = database ?? profile.Database
            }, JsonOptions)
            : JsonSerializer.Serialize(new { connection = profile.Name, server = profile.Server }, JsonOptions);
        result.Content = [new TextContentBlock { Text = $"{ExecutedOnPrefix} {executedOn}" }, .. result.Content];
        return result;
    }

    /// <summary>
    /// 讀取選填字串參數：未帶或 JSON null 視為未指定（value = null）；
    /// 有帶但不是非空字串時回傳 false，避免錯誤的值被當成「沒帶」而退回目前連線。
    /// </summary>
    private static bool TryReadString(IDictionary<string, JsonElement>? arguments, string name, out string? value)
    {
        value = null;
        if (arguments == null || !arguments.TryGetValue(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return true;

        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
            return false;

        value = element.GetString()!.Trim();
        return true;
    }

    private static bool DeclaresParameter(McpServerTool tool, string name) =>
        tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
        && properties.TryGetProperty(name, out _);

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }]
    };
}
