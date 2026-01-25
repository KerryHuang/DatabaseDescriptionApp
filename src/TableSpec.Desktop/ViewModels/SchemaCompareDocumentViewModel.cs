using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TableSpec.Application.Services;
using TableSpec.Domain.Entities;
using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;
using TableSpec.Domain.Interfaces;
using TableSpec.Infrastructure.Services;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 連線設定檔項目 ViewModel（用於目標環境選擇）
/// </summary>
public partial class ProfileItemViewModel : ViewModelBase
{
    private readonly SchemaCompareDocumentViewModel _parent;

    public ConnectionProfile Profile { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isEnabled = true;

    public ProfileItemViewModel(ConnectionProfile profile, SchemaCompareDocumentViewModel parent)
    {
        Profile = profile;
        _parent = parent;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _parent.OnProfileSelectionChanged(this, value);
    }
}

/// <summary>
/// Schema Compare 文件 ViewModel（MDI Document）
/// </summary>
public partial class SchemaCompareDocumentViewModel : DocumentViewModel
{
    private readonly ISchemaCompareService? _schemaCompareService;
    private readonly ISchemaCollector? _schemaCollector;
    private readonly IConnectionManager? _connectionManager;
    private CancellationTokenSource? _cancellationTokenSource;

    public override string DocumentType => "SchemaCompare";
    public override string DocumentKey => DocumentType; // 只允許開啟一個實例

    #region 連線選擇

    /// <summary>
    /// 可用的連線設定檔清單（用於基準環境下拉選單）
    /// </summary>
    public ObservableCollection<ConnectionProfile> AvailableProfiles { get; } = [];

    /// <summary>
    /// 目標環境項目清單（包含選取狀態）
    /// </summary>
    public ObservableCollection<ProfileItemViewModel> TargetProfileItems { get; } = [];

    /// <summary>
    /// 已選擇要比對的目標環境清單（用於比對）
    /// </summary>
    public IReadOnlyList<ConnectionProfile> SelectedProfiles =>
        TargetProfileItems.Where(p => p.IsSelected).Select(p => p.Profile).ToList();

    /// <summary>
    /// 已選取的目標環境數量
    /// </summary>
    [ObservableProperty]
    private int _selectedProfileCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private ConnectionProfile? _baseProfile;

    [ObservableProperty]
    private string _currentBaseName = string.Empty;

    #endregion

    #region 比對結果

    /// <summary>
    /// 比對結果清單
    /// </summary>
    public ObservableCollection<SchemaComparison> ComparisonResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredDifferences))]
    private SchemaComparison? _selectedComparison;

    [ObservableProperty]
    private SchemaDifference? _selectedDifference;

    #endregion

    #region 風險篩選

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredDifferences))]
    private bool _showLowRisk = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredDifferences))]
    private bool _showMediumRisk = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredDifferences))]
    private bool _showHighRisk = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredDifferences))]
    private bool _showForbidden = true;

    /// <summary>
    /// 篩選後的差異清單
    /// </summary>
    public IReadOnlyList<SchemaDifference> FilteredDifferences
    {
        get
        {
            if (SelectedComparison == null)
                return Array.Empty<SchemaDifference>();

            return SelectedComparison.Differences
                .Where(d =>
                    (ShowLowRisk && d.RiskLevel == RiskLevel.Low) ||
                    (ShowMediumRisk && d.RiskLevel == RiskLevel.Medium) ||
                    (ShowHighRisk && d.RiskLevel == RiskLevel.High) ||
                    (ShowForbidden && d.RiskLevel == RiskLevel.Forbidden))
                .ToList();
        }
    }

    #endregion

    #region 統計

    /// <summary>
    /// 總差異數量
    /// </summary>
    public int TotalDifferenceCount => ComparisonResults.Sum(c => c.Differences.Count);

    #endregion

    #region 狀態

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportExcelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportHtmlCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportSqlCommand))]
    private bool _isComparing;

    [ObservableProperty]
    private int _progressPercentage;

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "就緒";

    #endregion

    /// <summary>
    /// 設計時建構函式
    /// </summary>
    public SchemaCompareDocumentViewModel()
    {
        Title = "結構比對";
        Icon = "🔄";
        CanClose = true;
    }

    /// <summary>
    /// DI 建構函式
    /// </summary>
    public SchemaCompareDocumentViewModel(
        ISchemaCompareService schemaCompareService,
        ISchemaCollector schemaCollector,
        IConnectionManager connectionManager) : this()
    {
        _schemaCompareService = schemaCompareService;
        _schemaCollector = schemaCollector;
        _connectionManager = connectionManager;

        LoadConnectionProfiles();
    }

    #region 初始化

    private void LoadConnectionProfiles()
    {
        AvailableProfiles.Clear();
        TargetProfileItems.Clear();

        var profiles = _connectionManager?.GetAllProfiles() ?? [];
        foreach (var profile in profiles)
        {
            AvailableProfiles.Add(profile);
            TargetProfileItems.Add(new ProfileItemViewModel(profile, this));
        }

        // 選擇目前的連線作為基準
        var currentProfile = _connectionManager?.GetCurrentProfile();
        if (currentProfile != null)
        {
            BaseProfile = AvailableProfiles.FirstOrDefault(p => p.Id == currentProfile.Id);
        }

        SelectedProfileCount = 0;
    }

    /// <summary>
    /// 當目標環境選取狀態變更時呼叫
    /// </summary>
    internal void OnProfileSelectionChanged(ProfileItemViewModel item, bool isSelected)
    {
        // 不能選擇與基準相同的環境
        if (isSelected && item.Profile.Id == BaseProfile?.Id)
        {
            item.IsSelected = false;
            return;
        }

        SelectedProfileCount = TargetProfileItems.Count(p => p.IsSelected);
        CompareCommand.NotifyCanExecuteChanged();
    }

    partial void OnBaseProfileChanged(ConnectionProfile? value)
    {
        CurrentBaseName = value?.Name ?? string.Empty;

        // 更新目標環境的啟用狀態
        foreach (var item in TargetProfileItems)
        {
            if (value != null && item.Profile.Id == value.Id)
            {
                // 基準環境不能作為目標
                item.IsSelected = false;
                item.IsEnabled = false;
            }
            else
            {
                item.IsEnabled = true;
            }
        }

        SelectedProfileCount = TargetProfileItems.Count(p => p.IsSelected);
        CompareCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region 比對命令

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        if (_schemaCompareService == null || _schemaCollector == null || _connectionManager == null)
            return;

        if (BaseProfile == null || SelectedProfiles.Count == 0)
            return;

        try
        {
            IsComparing = true;
            _cancellationTokenSource = new CancellationTokenSource();
            ProgressPercentage = 0;
            ProgressMessage = "準備比對...";
            StatusMessage = "比對中...";
            ComparisonResults.Clear();

            var ct = _cancellationTokenSource.Token;

            // 收集基準 Schema
            ProgressMessage = $"收集基準環境：{BaseProfile.Name}...";
            var baseConnectionString = _connectionManager.GetConnectionString(BaseProfile.Id);
            if (string.IsNullOrEmpty(baseConnectionString))
            {
                StatusMessage = "無法取得基準環境的連線字串";
                return;
            }

            var baseSchema = await _schemaCollector.CollectAsync(baseConnectionString, BaseProfile.Name, ct);
            ProgressPercentage = 20;

            // 收集目標 Schema
            var targetSchemas = new List<DatabaseSchema>();
            var step = 60 / Math.Max(1, SelectedProfiles.Count);
            var currentProgress = 20;

            foreach (var targetProfile in SelectedProfiles)
            {
                ct.ThrowIfCancellationRequested();

                ProgressMessage = $"收集目標環境：{targetProfile.Name}...";
                var targetConnectionString = _connectionManager.GetConnectionString(targetProfile.Id);
                if (string.IsNullOrEmpty(targetConnectionString))
                {
                    StatusMessage = $"無法取得 {targetProfile.Name} 的連線字串";
                    continue;
                }

                var targetSchema = await _schemaCollector.CollectAsync(targetConnectionString, targetProfile.Name, ct);
                targetSchemas.Add(targetSchema);

                currentProgress += step;
                ProgressPercentage = currentProgress;
            }

            // 執行比對
            ProgressMessage = "執行比對...";
            ProgressPercentage = 80;

            var results = await _schemaCompareService.CompareMultipleAsync(baseSchema, targetSchemas);

            // 更新結果
            foreach (var result in results)
            {
                ComparisonResults.Add(result);
            }

            ProgressPercentage = 100;
            ProgressMessage = string.Empty;

            var totalDiffs = ComparisonResults.Sum(c => c.Differences.Count);
            StatusMessage = $"比對完成：{ComparisonResults.Count} 個環境，共 {totalDiffs} 個差異";

            // 自動選擇第一個結果
            if (ComparisonResults.Count > 0)
            {
                SelectedComparison = ComparisonResults[0];
            }

            OnPropertyChanged(nameof(TotalDifferenceCount));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "比對已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"比對失敗：{ex.Message}";
        }
        finally
        {
            IsComparing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private bool CanCompare() =>
        BaseProfile != null &&
        SelectedProfileCount > 0 &&
        !IsComparing;

    #endregion

    #region 匯出命令

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportExcelAsync()
    {
        var storageProvider = App.GetStorageProvider();
        if (storageProvider == null) return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "匯出 Excel 報告",
            SuggestedFileName = $"SchemaCompare_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel 檔案") { Patterns = ["*.xlsx"] }
            ]
        });

        if (file != null)
        {
            try
            {
                StatusMessage = "匯出 Excel...";
                var exporter = new SchemaCompareExcelExporter();
                await exporter.ExportAsync(ComparisonResults, file.Path.LocalPath);
                StatusMessage = $"已匯出至：{file.Path.LocalPath}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"匯出失敗：{ex.Message}";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportHtmlAsync()
    {
        var storageProvider = App.GetStorageProvider();
        if (storageProvider == null) return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "匯出 HTML 報告",
            SuggestedFileName = $"SchemaCompare_{DateTime.Now:yyyyMMdd_HHmmss}.html",
            FileTypeChoices =
            [
                new FilePickerFileType("HTML 檔案") { Patterns = ["*.html"] }
            ]
        });

        if (file != null)
        {
            try
            {
                StatusMessage = "匯出 HTML...";
                var exporter = new SchemaCompareHtmlExporter();
                await exporter.ExportAsync(ComparisonResults, file.Path.LocalPath);
                StatusMessage = $"已匯出至：{file.Path.LocalPath}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"匯出失敗：{ex.Message}";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportSqlAsync()
    {
        var storageProvider = App.GetStorageProvider();
        if (storageProvider == null) return;

        var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "選擇匯出目錄",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            try
            {
                StatusMessage = "產生同步報告...";
                var basePath = folder[0].Path.LocalPath;

                foreach (var comparison in ComparisonResults)
                {
                    var content = GenerateSyncReport(comparison);
                    var fileName = $"Sync_{comparison.TargetEnvironment}_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
                    var filePath = Path.Combine(basePath, fileName);

                    await File.WriteAllTextAsync(filePath, content);
                }

                StatusMessage = $"已匯出至：{basePath}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"匯出失敗：{ex.Message}";
            }
        }
    }

    /// <summary>
    /// 產生同步報告（SQL 格式的說明）
    /// </summary>
    private static string GenerateSyncReport(SchemaComparison comparison)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"-- Schema Compare 同步報告");
        sb.AppendLine($"-- 基準環境：{comparison.BaseEnvironment}");
        sb.AppendLine($"-- 目標環境：{comparison.TargetEnvironment}");
        sb.AppendLine($"-- 產生時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"-- 差異數量：{comparison.Differences.Count}");
        sb.AppendLine();

        var grouped = comparison.Differences.GroupBy(d => d.ObjectType);

        foreach (var group in grouped)
        {
            sb.AppendLine($"-- === {GetObjectTypeDisplayName(group.Key)} ({group.Count()}) ===");
            sb.AppendLine();

            foreach (var diff in group)
            {
                sb.AppendLine($"-- [{GetRiskLevelDisplayName(diff.RiskLevel)}] {GetDifferenceTypeDisplayName(diff.DifferenceType)}：{diff.ObjectName}");
                if (!string.IsNullOrEmpty(diff.Description))
                {
                    sb.AppendLine($"--   描述：{diff.Description}");
                }
                if (!string.IsNullOrEmpty(diff.PropertyName))
                {
                    sb.AppendLine($"--   屬性：{diff.PropertyName}");
                    sb.AppendLine($"--   來源值：{diff.SourceValue ?? "NULL"}");
                    sb.AppendLine($"--   目標值：{diff.TargetValue ?? "NULL"}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("-- 請根據上述差異手動撰寫同步腳本，或使用更完整的 Schema Compare 工具產生腳本。");

        return sb.ToString();
    }

    private static string GetObjectTypeDisplayName(SchemaObjectType objectType)
    {
        return objectType switch
        {
            SchemaObjectType.Table => "表格",
            SchemaObjectType.Column => "欄位",
            SchemaObjectType.Index => "索引",
            SchemaObjectType.Constraint => "約束",
            SchemaObjectType.View => "檢視表",
            SchemaObjectType.StoredProcedure => "預存程序",
            SchemaObjectType.Function => "函數",
            SchemaObjectType.Trigger => "觸發程序",
            _ => objectType.ToString()
        };
    }

    private static string GetDifferenceTypeDisplayName(DifferenceType differenceType)
    {
        return differenceType switch
        {
            DifferenceType.Added => "新增",
            DifferenceType.Modified => "修改",
            _ => differenceType.ToString()
        };
    }

    private static string GetRiskLevelDisplayName(RiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RiskLevel.Low => "低",
            RiskLevel.Medium => "中",
            RiskLevel.High => "高",
            RiskLevel.Forbidden => "禁止",
            _ => riskLevel.ToString()
        };
    }

    private bool CanExport() =>
        ComparisonResults.Count > 0 &&
        !IsComparing;

    #endregion

    #region 取消命令

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "正在取消...";
    }

    #endregion

    #region 刷新連線命令

    [RelayCommand]
    private void RefreshProfiles()
    {
        LoadConnectionProfiles();
        StatusMessage = "已重新載入連線設定檔";
    }

    #endregion

    #region 選擇環境命令

    [RelayCommand]
    private void ToggleProfile(ProfileItemViewModel item)
    {
        item.IsSelected = !item.IsSelected;
    }

    [RelayCommand]
    private void SelectAllProfiles()
    {
        foreach (var item in TargetProfileItems)
        {
            if (item.Profile.Id != BaseProfile?.Id)
            {
                item.IsSelected = true;
            }
        }
    }

    [RelayCommand]
    private void ClearSelectedProfiles()
    {
        foreach (var item in TargetProfileItems)
        {
            item.IsSelected = false;
        }
    }

    #endregion
}
