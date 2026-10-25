using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModShardLauncher.GUI.Services;

namespace ModShardLauncher.GUI.ViewModels;

public sealed class HomeViewModel
{
    public HomeViewModel(MainWindowViewModel main) => Main = main;

    public MainWindowViewModel Main { get; }
}

public sealed partial class ModsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    public ModsViewModel(MainWindowViewModel main)
    {
        _main = main;
        Mods = new ObservableCollection<ModItemViewModel>();
    }

    public MainWindowViewModel Main => _main;

    public ObservableCollection<ModItemViewModel> Mods { get; }

    public bool HasMods => Mods.Count > 0;

    public bool HasLoadErrors => _main.Services.Catalog.LoadErrors.Count > 0;

    public string LoadErrorsText => string.Join(Environment.NewLine, _main.Services.Catalog.LoadErrors);

    public string CountText => LocalizationService.Instance.Format("ModsCount", Mods.Count);

    public string ReloadLabel => LocalizationService.Instance["ModsReload"];

    public string PatchLabel => LocalizationService.Instance["ModsPatchSave"];

    public string EmptyText => LocalizationService.Instance["ModsEmpty"];

    public string ModsTitle => LocalizationService.Instance["ModsTitle"];

    public string ModsSubtitle => LocalizationService.Instance["ModsSubtitle"];

    public Task SyncAsync()
    {
        Mods.Clear();
        foreach (ModShardLauncher.ModFile mod in _main.Services.Catalog.Mods)
        {
            Mods.Add(new ModItemViewModel(mod));
        }

        NotifyState();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ReloadAsync() => _main.ReloadModsAsync();

    [RelayCommand]
    private Task PatchAndSaveAsync() => _main.PatchAndSaveAsync();

    public void NotifyState()
    {
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(HasLoadErrors));
        OnPropertyChanged(nameof(LoadErrorsText));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ReloadLabel));
        OnPropertyChanged(nameof(PatchLabel));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ModsTitle));
        OnPropertyChanged(nameof(ModsSubtitle));
    }

    /// <summary>Rebuilds row view models so localized labels and statuses are re-evaluated.</summary>
    public void RefreshLocalized()
    {
        foreach (ModItemViewModel mod in Mods)
        {
            mod.RefreshState();
        }

        NotifyState();
    }


}

public sealed partial class ModSourcesViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    public ModSourcesViewModel(MainWindowViewModel main)
    {
        _main = main;
        Sources = new ObservableCollection<ModSourceItemViewModel>();
    }

    public ObservableCollection<ModSourceItemViewModel> Sources { get; }

    public bool HasSources => Sources.Count > 0;

    public string CountText => LocalizationService.Instance.Format("SourcesCount", Sources.Count);

    public string EmptyText => LocalizationService.Instance["SourcesEmpty"];

    public string SourcesTitle => LocalizationService.Instance["SourcesTitle"];

    public string SourcesSubtitle => LocalizationService.Instance["SourcesSubtitle"];

    public Task SyncAsync()
    {
        Sources.Clear();
        foreach (ModShardLauncher.ModSource source in _main.Services.Catalog.ModSources)
        {
            Sources.Add(new ModSourceItemViewModel(source, _main));
        }

        NotifyState();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ReloadAsync() => _main.ReloadModsAsync();

    public void NotifyState()
    {
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(SourcesTitle));
        OnPropertyChanged(nameof(SourcesSubtitle));
    }

    public void RefreshLocalized()
    {
        foreach (ModSourceItemViewModel source in Sources)
        {
            source.RefreshLocalized();
        }

        NotifyState();
    }
}

public sealed record LanguageOption(string Display, string Culture, string SettingName);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    public SettingsViewModel(MainWindowViewModel main)
    {
        _main = main;
        Languages =
        [
            new LanguageOption("Auto", "auto", "auto"),
            new LanguageOption("English", "en", "English"),
            new LanguageOption("中文", "zh-Hans", "Chinese"),
            new LanguageOption("Русский", "ru", "Russian"),
        ];
        _selectedLanguage = main.Settings.LanguageChosen
            ? Languages.FirstOrDefault(x => x.Culture == LocalizationService.Instance.Culture.Name) ?? Languages[1]
            : Languages[0];
        _enableLogger = main.Settings.EnableLogger;
        _exportManifests = main.Settings.ExportManifests;
    }

    public MainWindowViewModel Main => _main;

    public IReadOnlyList<LanguageOption> Languages { get; }

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private bool _enableLogger;

    [ObservableProperty]
    private bool _exportManifests;

    public string SettingsTitle => LocalizationService.Instance["SettingsTitle"];
    public string LanguageLabel => LocalizationService.Instance["SettingsLanguage"];
    public string LanguageHint => LocalizationService.Instance["SettingsLanguageHint"];
    public string LoggerLabel => LocalizationService.Instance["SettingsLogger"];
    public string LoggerHint => LocalizationService.Instance["SettingsLoggerHint"];
    public string ExportLabel => LocalizationService.Instance["SettingsExport"];
    public string ExportHint => LocalizationService.Instance["SettingsExportHint"];
    public string FoldersLabel => LocalizationService.Instance["SettingsPaths"];
    public string DataFolderLabel => LocalizationService.Instance["SettingsDataFolder"];
    public string ModsFolderLabel => LocalizationService.Instance["SettingsModsFolder"];
    public string SourcesFolderLabel => LocalizationService.Instance["SettingsSourcesFolder"];
    public string LogsFolderLabel => LocalizationService.Instance["SettingsLogsFolder"];
    public string OpenLabel => LocalizationService.Instance["SettingsOpen"];
    public string AboutLabel => LocalizationService.Instance["SettingsAbout"];
    public string DataFolder => AppPaths.DataDirectory;
    public string ModsFolder => AppPaths.ModsDirectory;
    public string SourcesFolder => AppPaths.ModSourcesDirectory;
    public string LogsFolder => AppPaths.LogsDirectory;
    public string VersionText => $"{AppInfo.ProductName} {AppInfo.MslVersion}";
    public string UtmtText => $"UndertaleModTool {AppInfo.UtmtLibVersion}";

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value.Culture == "auto")
        {
            _main.Settings.Language = "auto";
            _main.Settings.LanguageChosen = false;
            LocalizationService.Instance.SetLanguage(LocalizationService.DetectSystemCulture());
        }
        else
        {
            _main.Settings.Language = value.SettingName;
            _main.Settings.LanguageChosen = true;
            LocalizationService.Instance.SetLanguage(value.Culture);
        }

        _main.SaveSettings();
    }

    partial void OnEnableLoggerChanged(bool value)
    {
        _main.Settings.EnableLogger = value;
        _main.Services.ApplyLogLevel();
        _main.SaveSettings();
    }

    partial void OnExportManifestsChanged(bool value)
    {
        _main.Settings.ExportManifests = value;
        _main.Services.GameData.ExportManifests = value;
        _main.SaveSettings();
    }

    [RelayCommand]
    private void OpenDataFolder() => _main.OpenFolder(AppPaths.DataDirectory);

    [RelayCommand]
    private void OpenModsFolder() => _main.OpenFolder(AppPaths.ModsDirectory);

    [RelayCommand]
    private void OpenSourcesFolder() => _main.OpenFolder(AppPaths.ModSourcesDirectory);

    [RelayCommand]
    private void OpenLogsFolder() => _main.OpenFolder(AppPaths.LogsDirectory);

    [RelayCommand]
    private Task ShowAboutAsync() => _main.ShowAboutAsync();

    public void RefreshLocalized()
    {
        OnPropertyChanged(nameof(SettingsTitle));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(LanguageHint));
        OnPropertyChanged(nameof(LoggerLabel));
        OnPropertyChanged(nameof(LoggerHint));
        OnPropertyChanged(nameof(ExportLabel));
        OnPropertyChanged(nameof(ExportHint));
        OnPropertyChanged(nameof(FoldersLabel));
        OnPropertyChanged(nameof(DataFolderLabel));
        OnPropertyChanged(nameof(ModsFolderLabel));
        OnPropertyChanged(nameof(SourcesFolderLabel));
        OnPropertyChanged(nameof(LogsFolderLabel));
        OnPropertyChanged(nameof(OpenLabel));
        OnPropertyChanged(nameof(AboutLabel));
    }
}
