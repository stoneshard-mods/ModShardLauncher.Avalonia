using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ModShardLauncher.GUI.Services;
using ModShardLauncher.GUI.ViewModels;
using ModShardLauncher.GUI.Views;

namespace ModShardLauncher.GUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppServices services = AppServices.Create();
            MainWindowViewModel viewModel = new(services);
            MainWindow window = new()
            {
                DataContext = viewModel,
            };

            services.Dialogs.Attach(window);
            window.Opened += async (_, _) => await viewModel.InitializeAsync();
            window.Closing += (_, _) =>
            {
                viewModel.SaveSettings();
                services.Shutdown();
            };

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
