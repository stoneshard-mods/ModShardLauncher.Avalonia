using ModShardLauncher.GUI.ViewModels;
using Serilog;
using Serilog.Events;

namespace ModShardLauncher.GUI.Services;

/// <summary>
/// Composition root: builds the services once, wires logging and localization, and exposes the
/// long lived collaborators to the view models. No service locator lookups from inside the UI.
/// </summary>
public sealed class AppServices
{
    private AppServices(
        UserSettings settings,
        SettingsStore settingsStore,
        GameDataService gameData,
        ModCatalog catalog,
        PatchRunner patcher,
        ModCompiler compiler,
        UiLogSink logSink,
        DialogService dialogs)
    {
        Settings = settings;
        SettingsStore = settingsStore;
        GameData = gameData;
        Catalog = catalog;
        Patcher = patcher;
        Compiler = compiler;
        LogSink = logSink;
        Dialogs = dialogs;
    }

    public static AppServices Current { get; private set; } = null!;

    public UserSettings Settings { get; }

    public SettingsStore SettingsStore { get; }

    public GameDataService GameData { get; }

    public ModCatalog Catalog { get; }

    public PatchRunner Patcher { get; }

    public ModCompiler Compiler { get; }

    public UiLogSink LogSink { get; }

    public DialogService Dialogs { get; }

    public static AppServices Create()
    {
        // Migrate before the logger and the settings store touch the new folder, otherwise the
        // freshly created log file would make the legacy logs folder look like a conflict.
        IReadOnlyList<string> migrated = AppPaths.MigratePortableLayout();
        AppPaths.EnsureCreated();

        UiLogSink logSink = new();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(AppPaths.LogsDirectory, $"log_{DateTime.Now:yyyyMMdd_HHmm}.txt"))
            .WriteTo.Sink(logSink)
            .CreateLogger();

        Log.Information("Using user data folder {Directory}", AppPaths.DataDirectory);
        foreach (string item in migrated)
        {
            Log.Information("Migrated {Item} from the portable layout", item);
        }

        SettingsStore settingsStore = new();
        UserSettings settings = settingsStore.Load();

        string culture = settings.LanguageChosen
            ? LocalizationService.CultureFor(settings.Language)
            : LocalizationService.DetectSystemCulture();
        LocalizationService.Instance.SetLanguage(culture);
        Log.Information("Using UI culture {Culture} (language setting: {Language})", culture, settings.Language);

        AppServices services = new(
            settings,
            settingsStore,
            new GameDataService(),
            new ModCatalog(),
            new PatchRunner(),
            new ModCompiler(),
            logSink,
            new DialogService());

        services.ApplyLogLevel();
        services.GameData.ExportManifests = settings.ExportManifests;

        Log.Information("Launching msl {{{0}}} using UTMT {{{1}}}", AppInfo.MslVersion, AppInfo.UtmtLibVersion);

        Current = services;
        return services;
    }

    public void ApplyLogLevel()
    {
        // Patching details are Information level and must always reach the log panel; the
        // setting only controls whether debug noise is shown.
        LogSink.SetMinimumLevel(Settings.EnableLogger ? LogEventLevel.Debug : LogEventLevel.Information);
    }

    public void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cannot save the settings");
        }
    }

    public void Shutdown() => Log.CloseAndFlush();
}
