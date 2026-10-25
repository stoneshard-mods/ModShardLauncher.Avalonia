using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ModShardLauncher.GUI.Views;

public partial class ErrorDialog : Window
{
    public ErrorDialog()
    {
        InitializeComponent();
    }

    public ErrorDialog(string title, string message, string? details)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        DetailsText.Text = details ?? string.Empty;
        DetailsBorder.IsVisible = !string.IsNullOrWhiteSpace(details);
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        string payload = $"{TitleText.Text}{Environment.NewLine}{Environment.NewLine}{MessageText.Text}{Environment.NewLine}{Environment.NewLine}{DetailsText.Text}";
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(payload);
        }
    }

    private void OpenLogs_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.LogsDirectory,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Opening the folder is a convenience; failing to do so must not mask the error dialog.
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
