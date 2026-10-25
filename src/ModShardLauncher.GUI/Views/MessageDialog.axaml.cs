using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModShardLauncher.GUI.Views;

/// <summary>Small modal used for info and confirmation prompts.</summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public MessageDialog(string title, string message, bool confirm)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        CancelButton.IsVisible = confirm;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
