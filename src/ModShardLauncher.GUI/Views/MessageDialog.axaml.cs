using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ModShardLauncher.GUI.Services;
using Serilog;

namespace ModShardLauncher.GUI.Views;

/// <summary>Small modal used for info and confirmation prompts.</summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    /// <param name="links">
    /// Optional addresses rendered as clickable links below the message, e.g. the page of this
    /// project and the original project it was refactored from.
    /// </param>
    public MessageDialog(string title, string message, bool confirm, IReadOnlyList<DialogLink>? links = null)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        CancelButton.IsVisible = confirm;

        if (links is not { Count: > 0 })
        {
            return;
        }

        foreach (DialogLink link in links)
        {
            LinksPanel.Children.Add(CreateLinkRow(link));
        }

        LinksPanel.IsVisible = true;
    }

    private static Control CreateLinkRow(DialogLink link)
    {
        HyperlinkButton address = new()
        {
            Content = link.Url,
            Padding = default(Thickness),
        };
        address.Click += (_, _) => OpenUrl(link.Url);

        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = link.Label, Classes = { "caption" } },
                address,
            },
        };
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Opening the browser is a convenience; the address stays selectable in the dialog.
            Log.Error(ex, "Cannot open {Url}", url);
        }
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
