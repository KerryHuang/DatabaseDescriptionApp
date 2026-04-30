using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.Domain.Entities.SchemaCompare;
using Specurai.Domain.Enums;
using Specurai.Domain.Interfaces;

namespace Specurai.Desktop.ViewModels;

/// <summary>
/// Schema Migration 主 ViewModel
/// </summary>
public partial class SchemaMigrationDocumentViewModel : DocumentViewModel
{
    private readonly ISchemaMigrationService? _migrationService;
    private readonly ISqlScriptGenerator? _scriptGenerator;
    private readonly ISchemaMigrationExecutor? _executor;
    private readonly IConnectionManager? _connectionManager;

    private MigrationAnalysis? _currentAnalysis;

    public override string DocumentType => "SchemaMigration";
    public override string DocumentKey => DocumentType;

    [ObservableProperty]
    private ConnectionProfile? _selectedBaseProfile;

    [ObservableProperty]
    private ConnectionProfile? _selectedTargetProfile;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _statusMessage = "請選擇基準資料庫與目標資料庫";

    [ObservableProperty]
    private MigrationReport? _lastReport;

    [ObservableProperty]
    private string? _analysisReport;

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];
    public ObservableCollection<MigrationDifferenceRowViewModel> DifferenceRows { get; } = [];
    public ObservableCollection<MigrationDifferenceRowViewModel> FilteredRows { get; } = [];

    private int _selectedExecutableCount;

    // 篩選屬性
    [ObservableProperty] private string _filterTableName = string.Empty;
    [ObservableProperty] private string _filterColumnName = string.Empty;

    // 多選篩選選項
    public IReadOnlyList<FilterOptionViewModel> RiskLevelFilters { get; } = CreateFilters(
        "🟢 低風險", "🟡 中風險", "🔴 高風險", "🔴 禁止");
    public IReadOnlyList<FilterOptionViewModel> ObjectTypeFilters { get; } = CreateFilters(
        "表格", "欄位", "索引", "約束", "檢視表", "預存程序", "函數", "觸發程序");
    [ObservableProperty] private IReadOnlyList<FilterOptionViewModel> _differenceTypeFilters = [];

    [ObservableProperty] private string _riskFilterLabel = "風險（1）▾";
    [ObservableProperty] private string _objectTypeFilterLabel = "物件類型（3）▾";
    [ObservableProperty] private string _differenceTypeFilterLabel = "差異類型 ▾";

    private static IReadOnlyList<FilterOptionViewModel> CreateFilters(params string[] labels) =>
        labels.Select(l => new FilterOptionViewModel { Label = l }).ToList();

    // 設計時建構函式
    public SchemaMigrationDocumentViewModel()
    {
        Title = "Schema Migration";
        Icon = "🔄";
    }

    public SchemaMigrationDocumentViewModel(
        ISchemaMigrationService migrationService,
        ISqlScriptGenerator scriptGenerator,
        ISchemaMigrationExecutor executor,
        IConnectionManager connectionManager)
    {
        Title = "Schema Migration";
        Icon = "🔄";
        _migrationService = migrationService;
        _scriptGenerator = scriptGenerator;
        _executor = executor;
        _connectionManager = connectionManager;

        SetDefaultFilters();
        SubscribeFilterEvents();
        LoadProfiles();
    }

    private void LoadProfiles()
    {
        ConnectionProfiles.Clear();
        foreach (var profile in _connectionManager?.GetAllProfiles() ?? [])
            ConnectionProfiles.Add(profile);
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        if (_migrationService == null || _connectionManager == null ||
            SelectedBaseProfile == null || SelectedTargetProfile == null)
            return;

        IsAnalyzing = true;
        StatusMessage = "正在分析 Schema 差異...";
        DifferenceRows.Clear();
        FilteredRows.Clear();
        AnalysisReport = null;
        _currentAnalysis = null;

        try
        {
            var baseConn = _connectionManager.GetConnectionString(SelectedBaseProfile.Id);
            var targetConn = _connectionManager.GetConnectionString(SelectedTargetProfile.Id);

            if (string.IsNullOrEmpty(baseConn))
            {
                StatusMessage = $"無法取得基準資料庫連線字串：{SelectedBaseProfile.Name}";
                return;
            }

            if (string.IsNullOrEmpty(targetConn))
            {
                StatusMessage = $"無法取得目標資料庫連線字串：{SelectedTargetProfile.Name}";
                return;
            }

            _currentAnalysis = await _migrationService.AnalyzeAsync(
                baseConn, targetConn,
                SelectedBaseProfile.Name, SelectedTargetProfile.Name);

            foreach (var diff in _currentAnalysis.Comparison.Differences)
            {
                var row = new MigrationDifferenceRowViewModel(diff);
                row.SelectionChanged += OnRowSelectionChanged;
                DifferenceRows.Add(row);
            }
            _selectedExecutableCount = DifferenceRows.Count(r => r.IsSelected && r.IsExecutable);
            AnalysisReport = _currentAnalysis.GenerateReport();
            RebuildDifferenceTypeFilters();
            ApplyFilter();

            var total = DifferenceRows.Count;
            var blocked = _currentAnalysis.BlockedDifferences.Count;
            StatusMessage = $"分析完成：共 {total} 項差異，其中 {blocked} 項高風險（不可執行）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"分析失敗：{ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
            AnalyzeCommand.NotifyCanExecuteChanged();
            DryRunCommand.NotifyCanExecuteChanged();
            ExecuteMigrationCommand.NotifyCanExecuteChanged();
            PreviewSqlCommand.NotifyCanExecuteChanged();
            DownloadSqlCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanAnalyze() =>
        !IsAnalyzing && !IsExecuting &&
        SelectedBaseProfile != null && SelectedTargetProfile != null &&
        SelectedBaseProfile.Id != SelectedTargetProfile.Id;

    partial void OnFilterTableNameChanged(string value) => ApplyFilter();
    partial void OnFilterColumnNameChanged(string value) => ApplyFilter();

    private void SetDefaultFilters()
    {
        // RiskLevelFilters 順序：低風險(0)、中風險(1)、高風險(2)、禁止(3)
        // 預設勾選：僅低風險
        RiskLevelFilters[0].IsSelected = true; // 🟢 低風險

        // ObjectTypeFilters 順序：表格(0)、欄位(1)、索引(2)、約束(3)、檢視表(4)...
        // 預設勾選：表格、欄位、檢視表
        ObjectTypeFilters[0].IsSelected = true; // 表格
        ObjectTypeFilters[1].IsSelected = true; // 欄位
        ObjectTypeFilters[4].IsSelected = true; // 檢視表
    }

    private void SubscribeFilterEvents()
    {
        foreach (var f in RiskLevelFilters.Concat(ObjectTypeFilters))
            f.SelectionChanged += _ => ApplyFilter();
    }

    private void RebuildDifferenceTypeFilters()
    {
        var labels = DifferenceRows
            .Select(r => r.DifferenceTypeText)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        DifferenceTypeFilters = CreateFilters(labels);
        foreach (var f in DifferenceTypeFilters)
            f.SelectionChanged += _ => ApplyFilter();
        DifferenceTypeFilterLabel = "差異類型 ▾";
    }

    private void ApplyFilter()
    {
        var activeRisk = RiskLevelFilters.Where(f => f.IsSelected).Select(f => f.Label).ToHashSet();
        var activeType = ObjectTypeFilters.Where(f => f.IsSelected).Select(f => f.Label).ToHashSet();
        var activeDiff = DifferenceTypeFilters.Where(f => f.IsSelected).Select(f => f.Label).ToHashSet();

        RiskFilterLabel = activeRisk.Count == 0 ? "風險 ▾" : $"風險（{activeRisk.Count}）▾";
        ObjectTypeFilterLabel = activeType.Count == 0 ? "物件類型 ▾" : $"物件類型（{activeType.Count}）▾";
        DifferenceTypeFilterLabel = activeDiff.Count == 0 ? "差異類型 ▾" : $"差異類型（{activeDiff.Count}）▾";

        var query = DifferenceRows.AsEnumerable();

        if (!string.IsNullOrEmpty(FilterTableName))
            query = query.Where(r => r.Difference.ObjectName.Contains(FilterTableName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(FilterColumnName))
            query = query.Where(r =>
                r.Difference.ObjectType != SchemaObjectType.Column ||
                ExtractColumnName(r.Difference.ObjectName).Contains(FilterColumnName, StringComparison.OrdinalIgnoreCase));
        if (activeRisk.Count > 0)
            query = query.Where(r => activeRisk.Contains(r.RiskLevelText));
        if (activeType.Count > 0)
            query = query.Where(r => activeType.Contains(r.ObjectTypeText));
        if (activeDiff.Count > 0)
            query = query.Where(r => activeDiff.Contains(r.DifferenceTypeText));

        // 預設排序：風險降序（禁止→高→中→低）→ 物件類型（表格優先）→ 物件名稱
        var sorted = query
            .OrderByDescending(r => (int)r.Difference.RiskLevel)
            .ThenBy(r => (int)r.Difference.ObjectType)
            .ThenBy(r => r.Difference.ObjectName)
            .ToList();

        FilteredRows.Clear();
        foreach (var row in sorted)
            FilteredRows.Add(row);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterTableName = string.Empty;
        FilterColumnName = string.Empty;
        foreach (var f in RiskLevelFilters.Concat(ObjectTypeFilters).Concat(DifferenceTypeFilters))
            f.IsSelected = false;
    }

    private void OnRowSelectionChanged(bool isNowSelected)
    {
        _selectedExecutableCount += isNowSelected ? 1 : -1;
        NotifySelectionCommands();
    }

    partial void OnLastReportChanged(MigrationReport? value)
        => OnPropertyChanged(nameof(ReportTitle));

    partial void OnAnalysisReportChanged(string? value)
        => DownloadAnalysisReportCommand.NotifyCanExecuteChanged();

    partial void OnSelectedBaseProfileChanged(ConnectionProfile? value)
        => AnalyzeCommand.NotifyCanExecuteChanged();

    partial void OnSelectedTargetProfileChanged(ConnectionProfile? value)
        => AnalyzeCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanExecuteMigration))]
    private async Task DryRunAsync()
    {
        if (_executor == null || _scriptGenerator == null ||
            _connectionManager == null || _currentAnalysis == null ||
            SelectedTargetProfile == null)
            return;

        var selected = FilteredRows
            .Where(r => r.IsSelected && r.IsExecutable)
            .Select(r => r.Difference)
            .ToList();

        if (selected.Count == 0)
        {
            StatusMessage = "未選取任何可執行的差異項目";
            return;
        }

        IsExecuting = true;
        StatusMessage = $"正在 Dry Run（共 {selected.Count} 項，不會實際提交）...";

        try
        {
            var script = _scriptGenerator.Generate(
                selected,
                _currentAnalysis.BaseSchema,
                _currentAnalysis.BaseSchema.ConnectionName,
                _currentAnalysis.TargetSchema.ConnectionName);

            var targetConn = _connectionManager.GetConnectionString(SelectedTargetProfile.Id);
            LastReport = await _executor.ExecuteAsync(script, targetConn ?? string.Empty, dryRun: true);

            StatusMessage = LastReport.IsSuccess
                ? $"Dry Run 通過：腳本語法正確，共 {LastReport.SuccessCount} 項（已自動回滾，無實際變更）"
                : $"Dry Run 失敗：{LastReport.ErrorMessage}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dry Run 失敗：{ex.Message}";
        }
        finally
        {
            IsExecuting = false;
            DownloadReportCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteMigration))]
    private async Task ExecuteMigrationAsync()
    {
        if (_executor == null || _scriptGenerator == null ||
            _connectionManager == null || _currentAnalysis == null ||
            SelectedTargetProfile == null)
            return;

        var selected = FilteredRows
            .Where(r => r.IsSelected && r.IsExecutable)
            .Select(r => r.Difference)
            .ToList();

        if (selected.Count == 0)
        {
            StatusMessage = "未選取任何可執行的差異項目";
            return;
        }

        IsExecuting = true;
        StatusMessage = $"正在執行 Migration（共 {selected.Count} 項）...";

        try
        {
            var script = _scriptGenerator.Generate(
                selected,
                _currentAnalysis.BaseSchema,
                _currentAnalysis.BaseSchema.ConnectionName,
                _currentAnalysis.TargetSchema.ConnectionName);

            var targetConn = _connectionManager.GetConnectionString(SelectedTargetProfile.Id);
            LastReport = await _executor.ExecuteAsync(script, targetConn ?? string.Empty);

            StatusMessage = LastReport.IsSuccess
                ? $"Migration 完成：{LastReport.SuccessCount} 項成功，{LastReport.SkippedCount} 項略過"
                : $"Migration 失敗（已自動回滾）：{LastReport.ErrorMessage}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"執行失敗：{ex.Message}";
        }
        finally
        {
            IsExecuting = false;
            DownloadReportCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanExecuteMigration() =>
        !IsAnalyzing && !IsExecuting && _selectedExecutableCount > 0;

    [RelayCommand(CanExecute = nameof(CanGenerateScript))]
    private void PreviewSql()
    {
        if (_scriptGenerator == null || _currentAnalysis == null) return;

        var selected = FilteredRows
            .Where(r => r.IsSelected && r.IsExecutable)
            .Select(r => r.Difference)
            .ToList();

        var script = _scriptGenerator.Generate(
            selected,
            _currentAnalysis.BaseSchema,
            _currentAnalysis.BaseSchema.ConnectionName,
            _currentAnalysis.TargetSchema.ConnectionName);

        var window = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        var preview = new Views.SqlPreviewWindow(script.ApplyScript);
        preview.ShowDialog(window!);

        StatusMessage = $"腳本預覽：{selected.Count} 項，共 {script.ApplyScript.Length} 字元";
    }

    [RelayCommand(CanExecute = nameof(CanGenerateScript))]
    private async Task DownloadSqlAsync()
    {
        if (_scriptGenerator == null || _currentAnalysis == null) return;

        var selected = FilteredRows
            .Where(r => r.IsSelected && r.IsExecutable)
            .Select(r => r.Difference)
            .ToList();

        var script = _scriptGenerator.Generate(
            selected,
            _currentAnalysis.BaseSchema,
            _currentAnalysis.BaseSchema.ConnectionName,
            _currentAnalysis.TargetSchema.ConnectionName);

        var window = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (window == null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "儲存 Migration SQL",
            SuggestedFileName = $"migration_{DateTime.Now:yyyyMMdd_HHmmss}.sql",
            FileTypeChoices = [new FilePickerFileType("SQL 檔案") { Patterns = ["*.sql"] }]
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(script.ApplyScript);
            StatusMessage = "SQL 腳本已儲存";
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private async Task DownloadReportAsync()
    {
        if (LastReport == null) return;

        var window = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (window == null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "儲存執行報告",
            SuggestedFileName = $"migration_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            FileTypeChoices = [new FilePickerFileType("文字檔案") { Patterns = ["*.txt"] }]
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync("Migration 執行報告");
            await writer.WriteLineAsync($"目標環境：{LastReport.TargetEnvironment}");
            await writer.WriteLineAsync($"執行時間：{LastReport.ExecutedAt:yyyy-MM-dd HH:mm:ss}");
            await writer.WriteLineAsync($"總耗時：{LastReport.TotalDuration.TotalSeconds:F2} 秒");
            await writer.WriteLineAsync($"結果：{(LastReport.IsSuccess ? "成功" : "失敗")}");
            await writer.WriteLineAsync(new string('-', 60));

            foreach (var entry in LastReport.Entries)
            {
                var duration = entry.Duration.HasValue
                    ? $"{entry.Duration.Value.TotalMilliseconds:F0}ms"
                    : "-";
                var status = entry.Status switch
                {
                    MigrationLogStatus.Success => "✅",
                    MigrationLogStatus.Failed => "❌",
                    MigrationLogStatus.Skipped => "⏭️",
                    MigrationLogStatus.HighRisk => "⚠️",
                    _ => "?"
                };
                await writer.WriteLineAsync(
                    $"{status} {entry.ObjectName} | {entry.Action} | {duration} | {entry.Note ?? entry.ErrorMessage ?? ""}");
            }

            StatusMessage = "報告已儲存";
        }
    }

    private bool CanGenerateScript() =>
        _currentAnalysis != null && _selectedExecutableCount > 0;

    private bool CanExportReport() => LastReport != null;

    [RelayCommand(CanExecute = nameof(CanDownloadAnalysisReport))]
    private async Task DownloadAnalysisReportAsync()
    {
        if (AnalysisReport == null) return;

        var window = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (window == null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "儲存分析建議報告",
            SuggestedFileName = $"migration_analysis_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            FileTypeChoices = [new FilePickerFileType("文字檔案") { Patterns = ["*.txt"] }]
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(AnalysisReport);
            StatusMessage = "分析報告已儲存";
        }
    }

    private bool CanDownloadAnalysisReport() => AnalysisReport != null;

    public string ReportTitle => LastReport?.IsDryRun == true
        ? "🧪 Dry Run 報告（未實際提交）"
        : "📋 執行報告";

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in DifferenceRows.Where(r => r.IsExecutable))
            row.IsSelected = true;
        _selectedExecutableCount = DifferenceRows.Count(r => r.IsExecutable);
        NotifySelectionCommands();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var row in DifferenceRows.Where(r => r.IsExecutable))
            row.IsSelected = false;
        _selectedExecutableCount = 0;
        NotifySelectionCommands();
    }

    private void NotifySelectionCommands()
    {
        DryRunCommand.NotifyCanExecuteChanged();
        ExecuteMigrationCommand.NotifyCanExecuteChanged();
        PreviewSqlCommand.NotifyCanExecuteChanged();
        DownloadSqlCommand.NotifyCanExecuteChanged();
    }

    private static string ExtractColumnName(string objectName)
    {
        var start = objectName.LastIndexOf(".[", StringComparison.Ordinal);
        if (start < 0) return objectName;
        var end = objectName.LastIndexOf(']');
        if (end <= start + 2) return objectName;
        return objectName.Substring(start + 2, end - start - 2);
    }
}
