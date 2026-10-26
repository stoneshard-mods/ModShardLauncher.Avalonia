using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModShardLauncher;
using ModShardLauncher.GUI.Services;
using ModShardLauncher.Mods;
using Serilog;

namespace ModShardLauncher.GUI.ViewModels;

/// <summary>
/// Shell view model: navigation, global commands, busy/status state and the live log feed.
/// Page view models receive it so they can reuse the same commands instead of duplicating them.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int LogEntryLimit = 2000;

    private readonly AppServices _services;

    /// <summary>Log lines waiting to be pushed to the UI, batched to survive thousands of events.</summary>
    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();

    private string _statusKey = "StatusReady";
    private object?[] _statusArgs = Array.Empty<object?>();
    private string _busyKey = string.Empty;
    private bool _catalogLoaded;

    /// <summary>
    /// True when the in-memory data was touched by a patch run and must be reloaded from disk
    /// before it can be used again.
    /// </summary>
    private bool _dataNeedsReload;

    private int _logFlushScheduled;

    /// <summary>Set while a cancellable operation runs; the busy overlay's cancel button drives it.</summary>
    private CancellationTokenSource? _busyCancellation;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;

        Home = new HomeViewModel(this);
        Mods = new ModsViewModel(this);
        Sources = new ModSourcesViewModel(this);
        SettingsPage = new SettingsViewModel(this);

        NavigationItems =
        [
            new NavItem(LocalizationService.Instance.Text("NavHome")),
            new NavItem(LocalizationService.Instance.Text("NavMods")),
            new NavItem(LocalizationService.Instance.Text("NavSources")),
        ];

        foreach (LogEntry entry in services.LogSink.History)
        {
            LogEntries.Add(entry);
        }

        services.LogSink.EntryWritten += OnLogEntry;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        _currentPage = Home;
        SetStatus("StatusReady");
        DataFileName = services.GameData.DataPath.Length > 0 ? Path.GetFileName(services.GameData.DataPath) : null;
    }

    public AppServices Services => _services;

    public UserSettings Settings => _services.Settings;

    public HomeViewModel Home { get; }

    public ModsViewModel Mods { get; }

    public ModSourcesViewModel Sources { get; }

    public SettingsViewModel SettingsPage { get; }

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public ObservableCollection<NavItem> NavigationItems { get; }

    /// <summary>Raised when a log line was appended, so the view can scroll to it.</summary>
    public event Action? LogAppended;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private int _selectedNavIndex;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _dataFileName;

    [ObservableProperty]
    private bool _isLogVisible = true;

    /// <summary>True while the running operation can be cancelled (the overlay shows its button).</summary>
    [ObservableProperty]
    private bool _canCancelBusy;

    public string VersionText => $"{AppInfo.ProductName} {AppInfo.MslVersion}";

    public string DataFileDisplay => DataFileName ?? LocalizationService.Instance["HomeNoData"];

    public string ModsCountDisplay => LocalizationService.Instance.Format("HomeStatsModsValue", _services.Catalog.Mods.Count);

    public string EnabledCountDisplay => LocalizationService.Instance.Format("HomeStatsEnabledValue", _services.Catalog.Mods.Count(m => m.Enabled));

    public string SourcesCountDisplay => LocalizationService.Instance.Format("HomeStatsSourcesValue", _services.Catalog.ModSources.Count);

    public string ModsStatusText => LocalizationService.Instance.Format("StatusModsCount", _services.Catalog.Mods.Count);

    public string EnabledStatusText => LocalizationService.Instance.Format("StatusEnabledCount", _services.Catalog.Mods.Count(m => m.Enabled));

    public bool HasData => _services.GameData.IsLoaded;

    public bool AreModsPresent => _services.Catalog.Mods.Count > 0;

    public string LogTitle => LocalizationService.Instance["LogTitle"];

    public string LogClearLabel => LocalizationService.Instance["LogClear"];

    public string LogEmptyText => LocalizationService.Instance["LogEmpty"];

    public string DataHeaderText => LocalizationService.Instance["StatusBarData"];

    partial void OnSelectedNavIndexChanged(int value)
    {
        // -1 means the page was changed from outside the sidebar (Settings is only in the menu).
        switch (value)
        {
            case 0:
                CurrentPage = Home;
                break;
            case 1:
                CurrentPage = Mods;
                break;
            case 2:
                CurrentPage = Sources;
                break;
        }
    }

    // ----- navigation -------------------------------------------------------

    [RelayCommand]
    private void ShowHome() => SelectedNavIndex = 0;

    [RelayCommand]
    private void ShowMods() => SelectedNavIndex = 1;

    [RelayCommand]
    private void ShowSources() => SelectedNavIndex = 2;

    [RelayCommand]
    private void ShowSettings()
    {
        // Settings lives in the top menu only, so clear the sidebar selection first.
        SelectedNavIndex = -1;
        CurrentPage = SettingsPage;
    }

    [RelayCommand]
    private void ToggleLog() => IsLogVisible = !IsLogVisible;

    /// <summary>
    /// Asks the running operation to stop. The pipeline only stops between mods (and before the
    /// data file is swapped in), so the button reports "cancelling" until it actually ends.
    /// </summary>
    [RelayCommand]
    private void CancelBusy()
    {
        if (_busyCancellation is not { IsCancellationRequested: false } cancellation)
        {
            return;
        }

        Log.Information("Cancelling the running operation");
        BusyText = LocalizationService.Instance["StatusCancelling"];

        try
        {
            cancellation.Cancel();
        }
        catch (AggregateException ex)
        {
            // A throwing cancellation callback must not break the UI thread.
            Log.Warning(ex, "A cancellation callback failed");
        }
    }

    // ----- file / patch operations -----------------------------------------

    [RelayCommand]
    public async Task OpenDataAsync()
    {
        string? path = await _services.Dialogs.PickOpenDataFileAsync();
        if (path is null)
        {
            return;
        }

        await RunBusyAsync("StatusLoading", async () =>
        {
            await _services.GameData.LoadAsync(path);
            _dataNeedsReload = false;
            DataFileName = Path.GetFileName(path);
            await ReloadCatalogCoreAsync();
            SetStatus("StatusLoaded", DataFileName);
        });
    }

    /// <summary>Patches every enabled mod, then asks for the destination file (same order as the WPF build).</summary>
    [RelayCommand]
    public async Task PatchAndSaveAsync()
    {
        if (!_services.GameData.IsLoaded)
        {
            await _services.Dialogs.ShowErrorAsync(
                LocalizationService.Instance["DialogErrorTitle"],
                LocalizationService.Instance["LoadDataWarning"]);
            return;
        }

        await RunBusyAsync(
            "StatusPatching",
            async cancellationToken =>
            {
                PatchTimings.Reset();
                try
                {
                    await PatchAndSaveCoreAsync(cancellationToken);
                }
                finally
                {
                    // Written even when the patch failed: a failed run still shows where the time went.
                    WritePatchTimings();
                }
            },
            cancellable: true);
    }

    private async Task PatchAndSaveCoreAsync(CancellationToken cancellationToken)
    {
        await ReloadPatchedDataAsync(cancellationToken);

        Log.Information("Patching {Count} enabled mod(s)", _services.Catalog.Mods.Count(m => m.Enabled));

        PatchDiagnostic? diagnostic;
        try
        {
            // Keep the UI thread free so the log panel streams the patch progress.
            diagnostic = await Task.Run(
                () => _services.Patcher.Patch(_services.Catalog.Mods, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The pipeline stops between mods: the loaded data is partially patched and unusable.
            _dataNeedsReload = true;
            Log.Warning("Patching cancelled; the loaded data is partially patched and will be reloaded");
            SetStatus("StatusCancelled");
            return;
        }
        catch (Exception ex)
        {
            // The pipeline may have touched the data before throwing.
            _dataNeedsReload = true;

            // The legacy code lets this path escape; keep the app alive and report it.
            Log.Error(ex, "Patching failed");
            await _services.Dialogs.ShowErrorAsync(
                LocalizationService.Instance["DialogErrorTitle"],
                ex.Message,
                ex.ToString());
            SetStatus("StatusPatchFailed");
            return;
        }

        // Even a failed run leaves the loaded data partially patched (and a successful one is
        // fully patched), so it can never be reused. The reload happens lazily, before the next
        // patch, instead of making the user wait ~6 seconds for a file they may not patch again.
        _dataNeedsReload = true;

        if (diagnostic is null)
        {
            Log.Information("Successfully patch vanilla");
            string? destination = await _services.Dialogs.PickSaveDataFileAsync(_services.GameData.DataPath);
            if (destination is not null)
            {
                if (IsSameFile(destination, _services.GameData.DataPath))
                {
                    // Never overwrite the vanilla file that is currently loaded; the user
                    // still needs it for the next patch run.
                    Log.Warning("Refusing to overwrite the source data file {Path}", destination);
                    await _services.Dialogs.ShowErrorAsync(
                        LocalizationService.Instance["DialogErrorTitle"],
                        LocalizationService.Instance["SaveOverwriteSourceError"]);
                    SetStatus("StatusReady");
                }
                else
                {
                    Log.Information("Saving patched data to {Path}", destination);
                    try
                    {
                        await _services.GameData.SaveAsync(destination, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Warning("Saving cancelled; {Path} was not written", destination);
                        SetStatus("StatusCancelled");
                        return;
                    }

                    string? directory = Path.GetDirectoryName(destination);
                    if (directory is not null)
                    {
                        LootTableRegistry.SaveLootTables(directory);
                    }

                    SetStatus("StatusSaved", Path.GetFileName(destination));
                }
            }
            else
            {
                Log.Information("Saved cancelled.");
                SetStatus("StatusReady");
            }
        }
        else
        {
            Log.Information("Failed patching vanilla");
            await _services.Dialogs.ShowErrorAsync(diagnostic.Title, diagnostic.Summary, diagnostic.Details);
            SetStatus("StatusPatchFailed");
        }

        await ReloadCatalogCoreAsync();
    }

    /// <summary>
    /// Writes the stage breakdown next to the log files and surfaces a one line summary, so a slow
    /// patch can be diagnosed after the fact instead of by re-running with a debugger.
    /// </summary>
    private void WritePatchTimings()
    {
        if (PatchTimings.IsEmpty)
        {
            return;
        }

        Log.Debug("{Timings}", PatchTimings.ToSummary());

        string suffix = string.Empty;
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            string path = Path.Combine(
                AppPaths.LogsDirectory,
                $"timings_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, PatchTimings.ToJson());
            suffix = $" (details: logs/{Path.GetFileName(path)})";
        }
        catch (Exception ex)
        {
            // Timing output must never fail a patch run.
            Log.Warning(ex, "Cannot write the patch timings file");
        }

        Log.Information("{Timings}{Details}", PatchTimings.ToCompactSummary(), suffix);
    }

    /// <summary>
    /// Reloads the vanilla file when the previous patch run left the in-memory data dirty. Unlike
    /// the old unconditional reload it only costs time when another patch is actually requested.
    /// </summary>
    private async Task ReloadPatchedDataAsync(CancellationToken cancellationToken)
    {
        if (!_dataNeedsReload || string.IsNullOrEmpty(_services.GameData.DataPath))
        {
            return;
        }

        Log.Information("Reloading {Path} before patching", _services.GameData.DataPath);
        await _services.GameData.LoadAsync(_services.GameData.DataPath, cancellationToken);
        _dataNeedsReload = false;
    }

    [RelayCommand]
    public async Task ReloadModsAsync()
    {
        await RunBusyAsync("StatusReloading", async () =>
        {
            await ReloadCatalogCoreAsync();
            SetStatus("StatusReady");
        });
    }

    [RelayCommand]
    public async Task CompileAsync(ModSource source)
    {
        await RunBusyAsync("StatusCompiling", async () =>
        {
            ModCompileResult result = _services.Compiler.Compile(source.Path);
            if (result.Succeeded)
            {
                await ReloadCatalogCoreAsync();
                SetStatus("StatusCompiled", source.Name);
            }
            else
            {
                await _services.Dialogs.ShowErrorAsync(
                    LocalizationService.Instance["DialogErrorTitle"],
                    LocalizationService.Instance.Format("CompileFailed", source.Name));
                SetStatus("StatusCompileFailed");
            }
        });
    }

    // ----- misc commands ----------------------------------------------------

    [RelayCommand]
    private void OpenModsFolder() => OpenFolder(AppPaths.ModsDirectory);

    [RelayCommand]
    private void OpenSourcesFolder() => OpenFolder(AppPaths.ModSourcesDirectory);

    [RelayCommand]
    private void OpenLogsFolder() => OpenFolder(AppPaths.LogsDirectory);

    [RelayCommand]
    public Task ShowAboutAsync() => _services.Dialogs.ShowInfoAsync(
        LocalizationService.Instance["AboutTitle"],
        LocalizationService.Instance.Format("AboutText", AppInfo.MslVersion, AppInfo.UtmtLibVersion));

    [RelayCommand]
    private void ClearLog()
    {
        // Drop the queued lines too, otherwise a running patch would repopulate the panel.
        while (_pendingLogs.TryDequeue(out _))
        {
        }

        LogEntries.Clear();
    }

    [RelayCommand]
    private void Exit()
    {
        SaveSettings();
        _services.Shutdown();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    // ----- helpers ----------------------------------------------------------

    public void OpenFolder(string path) => _services.Dialogs.OpenFolder(path);

    private static bool IsSameFile(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public void SaveSettings()
    {
        if (_catalogLoaded)
        {
            // The enabled list is a snapshot of the current checkboxes, not an append-only log.
            _services.Settings.EnabledMods = ModCatalog.GetEnabledModNames(_services.Catalog.Mods);
        }

        _services.SaveSettings();
    }

    /// <summary>
    /// Runs the reload workflow once when the shell is shown, so the mod list is populated on
    /// startup without the user having to press "Reload".
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await ReloadCatalogCoreAsync(applySavedEnabledMods: true);
            SetStatus("StatusReady");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Initial mod reload failed");
            SetStatus("StatusFailed");
        }
    }

    public void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(DataFileDisplay));
        OnPropertyChanged(nameof(ModsCountDisplay));
        OnPropertyChanged(nameof(EnabledCountDisplay));
        OnPropertyChanged(nameof(SourcesCountDisplay));
        OnPropertyChanged(nameof(ModsStatusText));
        OnPropertyChanged(nameof(EnabledStatusText));
        OnPropertyChanged(nameof(LogTitle));
        OnPropertyChanged(nameof(LogClearLabel));
        OnPropertyChanged(nameof(LogEmptyText));
        OnPropertyChanged(nameof(DataHeaderText));
        RecomputeStatus();
        Mods.RefreshLocalized();
        Sources.RefreshLocalized();
        SettingsPage.RefreshLocalized();
    }

    private async Task ReloadCatalogCoreAsync(bool applySavedEnabledMods = false)
    {
        _services.Catalog.Reload();
        if (applySavedEnabledMods)
        {
            _services.Catalog.ApplyEnabledMods(_services.Settings.EnabledMods);
        }

        Log.Information("Loaded {Mods} mod(s) and {Sources} mod source(s)",
            _services.Catalog.Mods.Count, _services.Catalog.ModSources.Count);
        foreach (string error in _services.Catalog.LoadErrors)
        {
            Log.Warning(error);
        }

        await Mods.SyncAsync();
        await Sources.SyncAsync();
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(AreModsPresent));
        OnPropertyChanged(nameof(DataFileDisplay));
        OnPropertyChanged(nameof(ModsCountDisplay));
        OnPropertyChanged(nameof(EnabledCountDisplay));
        OnPropertyChanged(nameof(SourcesCountDisplay));
        OnPropertyChanged(nameof(ModsStatusText));
        OnPropertyChanged(nameof(EnabledStatusText));
        _catalogLoaded = true;
    }

    private Task RunBusyAsync(string busyKey, Func<Task> action) =>
        RunBusyAsync(busyKey, _ => action(), cancellable: false);

    private async Task RunBusyAsync(string busyKey, Func<CancellationToken, Task> action, bool cancellable)
    {
        if (IsBusy)
        {
            return;
        }

        using CancellationTokenSource cancellation = new();
        _busyCancellation = cancellation;

        IsBusy = true;
        CanCancelBusy = cancellable;
        _busyKey = busyKey;
        BusyText = LocalizationService.Instance[busyKey];

        Exception? failure = null;
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancelling is a user decision, not a failure: no error dialog, and the data is
            // already marked for a reload by the operation that was interrupted.
            Log.Warning("The running operation was cancelled");
            SetStatus("StatusCancelled");
        }
        catch (Exception ex)
        {
            failure = ex;
            Log.Error(ex, "The operation failed");
            SetStatus("StatusFailed");
        }
        finally
        {
            // Always leave the busy state before any dialog is shown, so a failed operation can
            // never leave the progress overlay covering the window.
            IsBusy = false;
            CanCancelBusy = false;
            _busyCancellation = null;
            BusyText = string.Empty;
            _busyKey = string.Empty;
        }

        if (failure is not null)
        {
            try
            {
                await _services.Dialogs.ShowErrorAsync(
                    LocalizationService.Instance["DialogErrorTitle"],
                    failure.Message,
                    failure.ToString());
            }
            catch (Exception dialogException)
            {
                // A broken dialog must not crash the command; the failure is already logged.
                Log.Error(dialogException, "Cannot display the error dialog");
            }
        }
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        RecomputeStatus();
    }

    private void RecomputeStatus()
    {
        StatusText = _statusArgs.Length == 0
            ? LocalizationService.Instance[_statusKey]
            : LocalizationService.Instance.Format(_statusKey, _statusArgs);

        if (_busyKey.Length > 0)
        {
            BusyText = LocalizationService.Instance[_busyKey];
        }
    }

    private void OnLogEntry(LogEntry entry)
    {
        // Serilog calls this on the patching thread, once per log line (thousands per patch).
        // Queue the entry and flush in a single UI-thread batch instead of posting one
        // dispatcher operation per line.
        _pendingLogs.Enqueue(entry);
        if (Interlocked.Exchange(ref _logFlushScheduled, 1) == 0)
        {
            Dispatcher.UIThread.Post(FlushPendingLogs);
        }
    }

    private void FlushPendingLogs()
    {
        Interlocked.Exchange(ref _logFlushScheduled, 0);

        int added = 0;
        while (_pendingLogs.TryDequeue(out LogEntry entry))
        {
            LogEntries.Add(entry);
            added++;
        }

        int excess = LogEntries.Count - LogEntryLimit;
        for (int i = 0; i < excess; i++)
        {
            LogEntries.RemoveAt(0);
        }

        if (added > 0)
        {
            LogAppended?.Invoke();
        }
    }
}
