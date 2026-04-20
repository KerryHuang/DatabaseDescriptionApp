using System;
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

    public ObservableCollection<ConnectionProfile> ConnectionProfiles { get; } = [];
    public ObservableCollection<MigrationDifferenceRowViewModel> DifferenceRows { get; } = [];

    private int _selectedExecutableCount;

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
            ExecuteMigrationCommand.NotifyCanExecuteChanged();
            PreviewSqlCommand.NotifyCanExecuteChanged();
            DownloadSqlCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanAnalyze() =>
        !IsAnalyzing && !IsExecuting &&
        SelectedBaseProfile != null && SelectedTargetProfile != null &&
        SelectedBaseProfile.Id != SelectedTargetProfile.Id;

    private void OnRowSelectionChanged(bool isNowSelected)
    {
        _selectedExecutableCount += isNowSelected ? 1 : -1;
        NotifySelectionCommands();
    }

    partial void OnLastReportChanged(MigrationReport? value)
        => OnPropertyChanged(nameof(ReportTitle));

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

        var selected = DifferenceRows
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

        var selected = DifferenceRows
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

        var selected = DifferenceRows
            .Where(r => r.IsSelected && r.IsExecutable)
            .Select(r => r.Difference)
            .ToList();

        var script = _scriptGenerator.Generate(
            selected,
            _currentAnalysis.BaseSchema,
            _currentAnalysis.BaseSchema.ConnectionName,
            _currentAnalysis.TargetSchema.ConnectionName);

        StatusMessage = $"腳本已產生（{selected.Count} 項，共 {script.ApplyScript.Length} 字元）";
    }

    [RelayCommand(CanExecute = nameof(CanGenerateScript))]
    private async Task DownloadSqlAsync()
    {
        if (_scriptGenerator == null || _currentAnalysis == null) return;

        var selected = DifferenceRows
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
        ExecuteMigrationCommand.NotifyCanExecuteChanged();
        PreviewSqlCommand.NotifyCanExecuteChanged();
        DownloadSqlCommand.NotifyCanExecuteChanged();
    }
}
