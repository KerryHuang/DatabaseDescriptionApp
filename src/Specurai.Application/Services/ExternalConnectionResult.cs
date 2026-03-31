using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// 外部連線來源同步結果
/// </summary>
/// <param name="Profiles">成功取得的連線設定清單</param>
/// <param name="FailedItems">解析失敗的項目識別名稱清單</param>
public record ExternalConnectionResult(
    IReadOnlyList<ConnectionProfile> Profiles,
    IReadOnlyList<string> FailedItems);
