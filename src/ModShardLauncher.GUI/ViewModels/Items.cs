using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModShardLauncher.GUI.Services;
using ModShardLauncher.Mods;

namespace ModShardLauncher.GUI.ViewModels;

/// <summary>One entry of the left navigation rail.</summary>
public sealed class NavItem
{
    public NavItem(LocalizedText title)
    {
        Title = title;
    }

    public LocalizedText Title { get; }
}

/// <summary>A single row of the Mods page.</summary>
public sealed partial class ModItemViewModel : ObservableObject
{
    private readonly ModFile _mod;

    public ModItemViewModel(ModFile mod)
    {
        _mod = mod;
        Icon = DecodeIcon(mod.Icon);
    }

    public string Name => _mod.instance?.Name ?? _mod.Name;

    public string Author => string.IsNullOrWhiteSpace(_mod.instance?.Author) ? "—" : _mod.instance.Author;

    public string Description => string.IsNullOrWhiteSpace(_mod.instance?.Description) ? "—" : _mod.instance.Description;

    public string Version => _mod.instance?.Version ?? _mod.Version;

    public string PackageVersion => _mod.Version;

    public bool Enabled
    {
        get => _mod.Enabled;
        set
        {
            if (_mod.Enabled == value)
            {
                return;
            }

            _mod.Enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public Bitmap? Icon { get; }

    public bool HasIcon => Icon is not null;

    public IBrush StatusBrush => _mod.PatchStatus switch
    {
        PatchStatus.Success => new SolidColorBrush(Color.Parse("#59C08A")),
        PatchStatus.Patching => new SolidColorBrush(Color.Parse("#E8B85C")),
        _ => new SolidColorBrush(Color.Parse("#6B7280")),
    };

    public string StatusText => _mod.PatchStatus switch
    {
        PatchStatus.Patching => LocalizationService.Instance["ModStatusPatching"],
        PatchStatus.Success => LocalizationService.Instance["ModStatusPatched"],
        _ => LocalizationService.Instance["ModStatusIdle"],
    };

    /// <summary>Called after a patch run or a language change.</summary>
    public void RefreshState()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(Enabled));
    }

    private static Bitmap? DecodeIcon(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using MemoryStream stream = new(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>A single row of the ModSources page.</summary>
public sealed partial class ModSourceItemViewModel : ObservableObject
{
    private readonly ModSource _source;
    private readonly MainWindowViewModel _main;

    public ModSourceItemViewModel(ModSource source, MainWindowViewModel main)
    {
        _source = source;
        _main = main;
    }

    public string Name => _source.Name;

    public string Folder => _source.Path;

    public string CompileLabel => LocalizationService.Instance["SourcesCompile"];

    public string OpenLabel => LocalizationService.Instance["SourcesOpenFolder"];

    [RelayCommand]
    private Task CompileAsync() => _main.CompileAsync(_source);

    [RelayCommand]
    private void OpenFolder() => _main.OpenFolder(_source.Path);

    public void RefreshLocalized()
    {
        OnPropertyChanged(nameof(CompileLabel));
        OnPropertyChanged(nameof(OpenLabel));
    }
}
