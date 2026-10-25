namespace ModShardLauncher;

/// <summary>
/// Per-user data layout. Everything the launcher writes at runtime - <c>Settings.json</c>, mods,
/// mod sources and logs - lives under <see cref="DataDirectory"/>, which defaults to
/// <c>%LOCALAPPDATA%\ModShardLauncher</c>. Keeping user data out of the installation folder means
/// updating or reinstalling the application never touches the user's mods or settings.
/// </summary>
public static class AppPaths
{
    /// <summary>Name of the data folder inside the per-user local application data folder.</summary>
    public const string DataFolderName = "ModShardLauncher";

    /// <summary>
    /// Root of every writable path. Defaults to <see cref="GetDefaultDataDirectory"/>; the CLI can
    /// override it to keep several isolated mod sets.
    /// </summary>
    public static string DataDirectory { get; set; } = GetDefaultDataDirectory();

    public static string ModsDirectory => Path.Combine(DataDirectory, "Mods");

    public static string ModSourcesDirectory => Path.Combine(DataDirectory, "ModSources");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string SettingsFile => Path.Combine(DataDirectory, "Settings.json");

    public static string ExportDirectory => Path.Combine(DataDirectory, "export");

    /// <summary>
    /// <c>%LOCALAPPDATA%\ModShardLauncher</c>. The special folder also resolves on other
    /// platforms (the XDG data directory), so the CLI and the tests keep working outside Windows.
    /// </summary>
    public static string GetDefaultDataDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(localAppData)
            ? Path.Combine(Environment.CurrentDirectory, DataFolderName)
            : Path.Combine(localAppData, DataFolderName);
    }

    /// <summary>Creates the data folder and the writable subfolders the application expects.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ModsDirectory);
        Directory.CreateDirectory(ModSourcesDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>Folders that belong to the pre-1.0 portable layout inside the installation folder.</summary>
    private static readonly string[] PortableDataFolders = ["Mods", "ModSources", "logs", "export"];

    /// <summary>
    /// Moves data written by the pre-1.0 portable layout (next to the executable) into the new data
    /// folder. An item is only migrated when the destination does not have it yet, so an existing
    /// data folder is never overwritten, and a locked or read-only file never blocks startup.
    /// </summary>
    /// <param name="legacyDirectory">
    /// Folder that contains the old layout; defaults to the application folder.
    /// </param>
    /// <returns>The names of the items that were migrated, empty when there was nothing to do.</returns>
    public static IReadOnlyList<string> MigratePortableLayout(string? legacyDirectory = null)
    {
        string legacy = Path.GetFullPath(legacyDirectory ?? AppContext.BaseDirectory);
        if (IsSameDirectory(legacy, DataDirectory))
        {
            return Array.Empty<string>();
        }

        List<string> migrated = new();
        if (TryMigrate(Path.Combine(legacy, "Settings.json"), SettingsFile))
        {
            migrated.Add("Settings.json");
        }

        foreach (string folder in PortableDataFolders)
        {
            if (TryMigrate(Path.Combine(legacy, folder), Path.Combine(DataDirectory, folder)))
            {
                migrated.Add(folder);
            }
        }

        return migrated;
    }

    private static bool TryMigrate(string source, string destination)
    {
        if ((!File.Exists(source) && !Directory.Exists(source)) || HasContent(destination))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(DataDirectory);
            if (File.Exists(source))
            {
                File.Move(source, destination);
                return true;
            }

            if (Directory.Exists(destination))
            {
                // Left behind by an earlier start that already created the empty folder structure.
                Directory.Delete(destination);
            }

            Directory.Move(source, destination);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The user can still copy the folder by hand; keep the original in place.
            return false;
        }
    }

    private static bool HasContent(string path) =>
        File.Exists(path) ||
        (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any());

    private static bool IsSameDirectory(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}
