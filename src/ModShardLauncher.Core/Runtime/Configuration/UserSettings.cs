using Newtonsoft.Json;

namespace ModShardLauncher;

/// <summary>
/// Persisted user preferences. Field names and the JSON shape are kept identical to the WPF
/// version so an existing <c>Settings.json</c> keeps working after the upgrade. The file lives in
/// the user data folder (<see cref="AppPaths.SettingsFile"/>), not next to the executable.
/// </summary>
public class UserSettings
{
    /// <summary>Language name, or "auto" to follow the Windows display language.</summary>
    public string Language = "auto";

    /// <summary>True once the user picked a language; legacy settings default to automatic detection.</summary>
    public bool LanguageChosen = false;

    public bool EnableLogger = true;

    /// <summary>When enabled, loading data.win also writes the item/weapon/armor CSV manifests.</summary>
    public bool ExportManifests = false;

    public List<string> EnabledMods = new();
}

/// <summary>Loads and saves <see cref="UserSettings"/> from <see cref="AppPaths.SettingsFile"/>.</summary>
public sealed class SettingsStore
{
    public UserSettings Load()
    {
        if (!File.Exists(AppPaths.SettingsFile))
        {
            return new UserSettings();
        }

        string json = File.ReadAllText(AppPaths.SettingsFile);
        return JsonConvert.DeserializeObject<UserSettings>(json) ?? new UserSettings();
    }

    public void Save(UserSettings settings)
    {
        // The data folder is created at startup; recreate it when the user removed it while the
        // application was running so a settings change is never lost.
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(AppPaths.SettingsFile, JsonConvert.SerializeObject(settings));
    }
}
