namespace Specurai.Domain.Entities;

/// <summary>
/// 應用程式更新檢查結果。
/// </summary>
public sealed class UpdateCheckResult
{
    /// <summary>新版本號（不含 "v" 前綴），例如 "1.7.0"。</summary>
    public required string NewVersion { get; init; }

    /// <summary>Release Notes 原始文字（可為 Markdown）。</summary>
    public required string ReleaseNotes { get; init; }

    /// <summary>GitHub Release 頁面 URL（macOS 手動下載時使用）。</summary>
    public required string ReleaseUrl { get; init; }

    /// <summary>是否可由應用程式自動套用（Win/Linux = true，macOS = false）。</summary>
    public required bool CanAutoApply { get; init; }
}
