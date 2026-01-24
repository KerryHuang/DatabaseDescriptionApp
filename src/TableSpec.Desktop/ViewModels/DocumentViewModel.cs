using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TableSpec.Desktop.ViewModels;

/// <summary>
/// 文件 ViewModel 基類，用於 MDI 子視窗
/// </summary>
public abstract partial class DocumentViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "未命名";

    [ObservableProperty]
    private string _icon = "";

    [ObservableProperty]
    private bool _canClose = true;

    /// <summary>
    /// 文件類型識別碼
    /// </summary>
    public abstract string DocumentType { get; }

    /// <summary>
    /// 文件唯一識別碼，用於防止重複開啟
    /// </summary>
    public virtual string DocumentKey => DocumentType;

    /// <summary>
    /// 關閉文件時觸發
    /// </summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    protected virtual void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
