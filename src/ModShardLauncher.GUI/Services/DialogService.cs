using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ModShardLauncher.GUI.Views;
using Serilog;

namespace ModShardLauncher.GUI.Services;

/// <summary>All window/file interactions the view models are allowed to perform.</summary>
public sealed class DialogService
{
    /// <summary>Default Steam installation folder for Stoneshard, used as the picker's start location.</summary>
    private const string StoneshardSteamFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Stoneshard";

    private Window? _owner;

    public void Attach(Window owner) => _owner = owner;

    public async Task<string?> PickOpenDataFileAsync()
    {
        if (_owner is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Instance["MenuOpenData"],
            AllowMultiple = false,
            FileTypeFilter = DataFileTypes,
            SuggestedStartLocation = await ResolveInitialFolderAsync(),
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Prefers the default Steam install of Stoneshard; when that folder does not exist the
    /// directory the application was started from is used instead.
    /// </summary>
    private async Task<IStorageFolder?> ResolveInitialFolderAsync()
    {
        if (_owner is null)
        {
            return null;
        }

        string[] candidates = [StoneshardSteamFolder, Environment.CurrentDirectory];
        foreach (string candidate in candidates)
        {
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            IStorageFolder? folder = await _owner.StorageProvider.TryGetFolderFromPathAsync(candidate);
            if (folder is not null)
            {
                return folder;
            }
        }

        return null;
    }

    /// <summary>
    /// Asks where to write the patched file. The suggestion never reuses the source file name,
    /// so pressing Enter by accident cannot overwrite the vanilla data.win.
    /// </summary>
    public async Task<string?> PickSaveDataFileAsync(string? sourceFilePath)
    {
        if (_owner is null)
        {
            return null;
        }

        string suggestedName = "data_patched.win";
        IStorageFolder? startLocation = null;
        if (!string.IsNullOrWhiteSpace(sourceFilePath))
        {
            string? directory = Path.GetDirectoryName(sourceFilePath);
            if (directory is not null)
            {
                startLocation = await _owner.StorageProvider.TryGetFolderFromPathAsync(directory);
            }

            string name = Path.GetFileNameWithoutExtension(sourceFilePath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                suggestedName = name + "_patched.win";
            }
        }

        IStorageFile? file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Instance["MenuSaveAs"],
            SuggestedFileName = suggestedName,
            DefaultExtension = "win",
            ShowOverwritePrompt = true,
            SuggestedStartLocation = startLocation,
            FileTypeChoices = DataFileTypes,
        });

        return file?.TryGetLocalPath();
    }

    public async Task ShowErrorAsync(string title, string message, string? details = null)
    {
        if (_owner is null)
        {
            return;
        }

        ErrorDialog dialog = new(title, message, details);
        await dialog.ShowDialog(_owner);
    }

    public async Task ShowInfoAsync(string title, string message)
    {
        if (_owner is null)
        {
            return;
        }

        MessageDialog dialog = new(title, message, confirm: false);
        await dialog.ShowDialog(_owner);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (_owner is null)
        {
            return false;
        }

        MessageDialog dialog = new(title, message, confirm: true);
        return await dialog.ShowDialog<bool>(_owner);
    }

    public void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cannot open the folder {0}", path);
        }
    }

    private static FilePickerFileType[] DataFileTypes =>
    [
        new("GameMaker data files")
        {
            Patterns = ["*.win", "*.unx", "*.ios", "*.droid", "audiogroup*.dat"],
        },
        FilePickerFileTypes.All,
    ];
}
