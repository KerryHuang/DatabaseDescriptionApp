namespace Specurai.Application.Services;

/// <summary>
/// 外部連線來源設定
/// </summary>
/// <param name="SourceDirectory">外部連線設定的根目錄路徑</param>
/// <param name="KeyFilePath">用於解密外部連線設定的金鑰檔案路徑（選填，空字串表示不使用）</param>
public record ExternalSourceConfig(
    string SourceDirectory,
    string KeyFilePath);
