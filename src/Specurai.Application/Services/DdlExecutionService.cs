using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;

namespace Specurai.Application.Services;

/// <summary>
/// DDL 執行服務實作
/// </summary>
public class DdlExecutionService : IDdlExecutionService
{
    private readonly IConnectionManager _connectionManager;
    private readonly ISqlDdlExecuteRepository _executeRepository;

    public DdlExecutionService(
        IConnectionManager connectionManager,
        ISqlDdlExecuteRepository executeRepository)
    {
        _connectionManager = connectionManager;
        _executeRepository = executeRepository;
    }

    public async Task<DdlExecutionResult> ExecuteAsync(
        string script, bool confirm, Guid? profileId = null, CancellationToken ct = default)
    {
        // 解析目標連線：指定 profileId 時不得靜默落回目前連線
        var profile = profileId == null
            ? _connectionManager.GetCurrentProfile()
            : _connectionManager.GetEnabledProfiles().FirstOrDefault(p => p.Id == profileId.Value);

        if (profile == null)
            return Reject(profileId == null
                ? "未設定目前連線，無法執行 DDL。"
                : "找不到指定的連線設定（可能已停用），請改選其他連線。");

        if (profile.Environment == DatabaseEnvironment.Production)
            return Reject($"連線「{profile.Name}」為正式環境，不允許執行 DDL。");

        var connectionString = profileId == null
            ? _connectionManager.GetCurrentConnectionString()
            : _connectionManager.GetConnectionString(profileId.Value);

        if (string.IsNullOrEmpty(connectionString))
            return Reject("無法取得連線字串，請確認連線設定。");

        return await _executeRepository.ExecuteAsync(script, connectionString, commit: confirm, ct);
    }

    private static DdlExecutionResult Reject(string reason)
        => new() { IsValid = false, RejectReason = reason };
}
