using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Specurai.Desktop.Views;

public partial class ConfirmDialog : Window
{
    public bool Result { get; private set; }

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string message) : this()
    {
        MessageText.Text = message;
    }

    /// <summary>
    /// 建立含警告橫幅的確認對話框。當 <paramref name="warningBanner"/> 非空白時，於訊息上方顯示紅色警告橫幅。
    /// </summary>
    public ConfirmDialog(string message, string? warningBanner) : this(message)
    {
        if (!string.IsNullOrEmpty(warningBanner))
        {
            WarningText.Text = warningBanner;
            WarningBanner.IsVisible = true;
        }
    }

    private void OnYesClick(object? sender, RoutedEventArgs e)
    {
        Result = true;
        Close();
    }

    private void OnNoClick(object? sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }
}
