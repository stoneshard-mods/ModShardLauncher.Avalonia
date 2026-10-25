using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ModShardLauncher.GUI.Views;

public partial class MainWindow : Window
{
    private const double PreferredWidth = 1240;
    private const double PreferredHeight = 820;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // The startup location logic applies its own size just after Opened, so clamp on the
        // next dispatcher turn to make the correction stick.
        Dispatcher.UIThread.Post(FitToScreen, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Clamps the initial size to the current monitor so the shell stays usable on 1280x800
    /// laptops and on scaled displays.
    /// </summary>
    private void FitToScreen()
    {
        Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        double availableWidth = screen.WorkingArea.Width / scale - 32;
        double availableHeight = screen.WorkingArea.Height / scale - 32;

        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Clamp(Width, MinWidth, Math.Max(MinWidth, Math.Min(PreferredWidth, availableWidth)));
        Height = Math.Clamp(Height, MinHeight, Math.Max(MinHeight, Math.Min(PreferredHeight, availableHeight)));
        Serilog.Log.Debug("Window fitted to {Width}x{Height}", Width, Height);
    }
}
