using Avalonia.Controls;
using ModShardLauncher.GUI.ViewModels;

namespace ModShardLauncher.GUI.Views;

public partial class LogPanel : UserControl
{
    private MainWindowViewModel? _viewModel;

    public LogPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LogAppended -= ScrollToEnd;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.LogAppended += ScrollToEnd;
        }
    }

    private void ScrollToEnd()
    {
        if (LogList.ItemCount > 0)
        {
            LogList.ScrollIntoView(LogList.ItemCount - 1);
        }
    }
}
