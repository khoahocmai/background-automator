using System.Windows;

namespace BackgroundAutomator.App.Services;

/// <summary>
/// Production WPF implementation of <see cref="IDialogService"/> using standard WPF MessageBox.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowWarning(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public bool Confirm(
        string title,
        string message,
        DialogSeverity severity = DialogSeverity.Warning)
    {
        var image = severity switch
        {
            DialogSeverity.Information => MessageBoxImage.Information,
            DialogSeverity.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.Warning
        };

        var result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.OKCancel,
            image,
            MessageBoxResult.Cancel);

        return result == MessageBoxResult.OK;
    }
}
