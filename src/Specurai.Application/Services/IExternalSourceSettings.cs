namespace Specurai.Application.Services;

/// <summary>
/// 外部連線來源設定服務介面
/// </summary>
public interface IExternalSourceSettings
{
    /// <summary>
    /// 載入外部連線來源設定
    /// </summary>
    /// <returns>外部連線來源設定</returns>
    ExternalSourceConfig Load();

    /// <summary>
    /// 儲存外部連線來源設定
    /// </summary>
    /// <param name="config">要儲存的設定</param>
    void Save(ExternalSourceConfig config);
}
