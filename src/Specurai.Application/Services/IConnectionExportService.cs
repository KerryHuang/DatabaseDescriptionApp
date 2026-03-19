using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// 連線設定匯出/匯入服務介面
/// </summary>
public interface IConnectionExportService
{
    /// <summary>
    /// 匯出連線設定為 JSON 位元組陣列
    /// </summary>
    byte[] ExportToJson(IReadOnlyList<ConnectionProfile> profiles, bool includePasswords);

    /// <summary>
    /// 匯出連線設定為加密位元組陣列
    /// </summary>
    byte[] ExportToEncryptedJson(IReadOnlyList<ConnectionProfile> profiles, string password, bool includePasswords);

    /// <summary>
    /// 從 JSON 匯入連線設定
    /// </summary>
    ConnectionExportData ImportFromJson(byte[] data);

    /// <summary>
    /// 從加密 JSON 匯入連線設定
    /// </summary>
    ConnectionExportData ImportFromEncryptedJson(byte[] data, string password);

    /// <summary>
    /// 偵測檔案是否為加密格式
    /// </summary>
    bool IsEncryptedFormat(byte[] data);
}
