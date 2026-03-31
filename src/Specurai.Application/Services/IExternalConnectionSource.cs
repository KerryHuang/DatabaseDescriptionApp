namespace Specurai.Application.Services;

/// <summary>
/// 外部連線來源同步服務介面
/// </summary>
public interface IExternalConnectionSource
{
    /// <summary>
    /// 從外部來源同步連線設定
    /// </summary>
    /// <returns>同步結果，包含成功的連線設定和失敗項目清單</returns>
    Task<ExternalConnectionResult> SyncAsync();
}
