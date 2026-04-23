using Specurai.Domain.Entities;

namespace Specurai.Application.Services;

/// <summary>
/// 應用程式自動更新服務抽象。
/// 實作依作業系統分派：Win/Linux 使用 Velopack、macOS 使用 GitHub API + 瀏覽器。
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// 檢查是否有比目前執行版本更新的穩定版本。
    /// 檢查失敗（離線、API 限流等）應回傳 null 並寫入 trace log，不得拋出例外。
    /// </summary>
    Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default);

    /// <summary>
    /// 下載已偵測到的更新封裝（僅 CanAutoApply = true 時有效）。
    /// </summary>
    Task DownloadAsync(IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 套用已下載的更新並重啟應用程式（僅 CanAutoApply = true 時有效）。
    /// </summary>
    void ApplyAndRestart();
}
