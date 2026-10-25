using ModShardLauncher;

namespace ModShardLauncherTest;

/// <summary>
/// User data lives in <c>%LOCALAPPDATA%\ModShardLauncher</c>, separate from the program files, so
/// updating or reinstalling the launcher never touches mods, mod sources, logs or settings.
/// </summary>
public class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"msl-paths-{Guid.NewGuid():N}");
    private readonly string _previousDataDirectory = AppPaths.DataDirectory;

    public AppPathsTests()
    {
        Directory.CreateDirectory(_root);
        AppPaths.DataDirectory = Path.Combine(_root, "data");
    }

    public void Dispose()
    {
        AppPaths.DataDirectory = _previousDataDirectory;
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // A leftover temp folder must not fail the test run.
        }
    }

    [Fact]
    public void DefaultDataDirectory_IsThePerUserLocalAppDataFolder()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
        {
            return;
        }

        Assert.Equal(Path.Combine(localAppData, "ModShardLauncher"), AppPaths.GetDefaultDataDirectory());
    }

    [Fact]
    public void EnsureCreated_PutsEveryWritablePathInsideTheDataDirectory()
    {
        AppPaths.EnsureCreated();

        Assert.True(Directory.Exists(AppPaths.DataDirectory));
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "Settings.json"), AppPaths.SettingsFile);
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "Mods"), AppPaths.ModsDirectory);
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "ModSources"), AppPaths.ModSourcesDirectory);
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "logs"), AppPaths.LogsDirectory);
        Assert.True(Directory.Exists(AppPaths.ModsDirectory));
        Assert.True(Directory.Exists(AppPaths.ModSourcesDirectory));
        Assert.True(Directory.Exists(AppPaths.LogsDirectory));
    }

    [Fact]
    public void MigratePortableLayout_MovesLegacyDataOnce()
    {
        string legacy = Path.Combine(_root, "program");
        Directory.CreateDirectory(Path.Combine(legacy, "Mods"));
        Directory.CreateDirectory(Path.Combine(legacy, "ModSources", "MyMod"));
        Directory.CreateDirectory(Path.Combine(legacy, "logs"));
        File.WriteAllText(Path.Combine(legacy, "Settings.json"), "{\"Language\":\"zh-Hans\"}");
        File.WriteAllText(Path.Combine(legacy, "Mods", "test.sml"), "MSLM");
        File.WriteAllText(Path.Combine(legacy, "ModSources", "MyMod", "Mod.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(legacy, "logs", "old.log"), "log");

        // Mimic a first start that already created the (empty) folder structure.
        AppPaths.EnsureCreated();

        IReadOnlyList<string> migrated = AppPaths.MigratePortableLayout(legacy);

        Assert.Contains("Settings.json", migrated);
        Assert.Contains("Mods", migrated);
        Assert.Contains("ModSources", migrated);
        Assert.Contains("logs", migrated);
        Assert.Equal("{\"Language\":\"zh-Hans\"}", File.ReadAllText(AppPaths.SettingsFile));
        Assert.Equal("MSLM", File.ReadAllText(Path.Combine(AppPaths.ModsDirectory, "test.sml")));
        Assert.True(File.Exists(Path.Combine(AppPaths.ModSourcesDirectory, "MyMod", "Mod.csproj")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(AppPaths.LogsDirectory, "old.log")));
        Assert.False(Directory.Exists(Path.Combine(legacy, "Mods")));
        Assert.False(File.Exists(Path.Combine(legacy, "Settings.json")));

        Assert.Empty(AppPaths.MigratePortableLayout(legacy));
    }

    [Fact]
    public void MigratePortableLayout_NeverOverwritesExistingData()
    {
        string legacy = Path.Combine(_root, "program");
        Directory.CreateDirectory(Path.Combine(legacy, "Mods"));
        File.WriteAllText(Path.Combine(legacy, "Settings.json"), "legacy");
        File.WriteAllText(Path.Combine(legacy, "Mods", "legacy.sml"), "legacy");

        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(AppPaths.SettingsFile, "current");
        Directory.CreateDirectory(AppPaths.ModsDirectory);
        File.WriteAllText(Path.Combine(AppPaths.ModsDirectory, "current.sml"), "current");

        Assert.Empty(AppPaths.MigratePortableLayout(legacy));
        Assert.Equal("current", File.ReadAllText(AppPaths.SettingsFile));
        Assert.True(File.Exists(Path.Combine(AppPaths.ModsDirectory, "current.sml")));
        Assert.False(File.Exists(Path.Combine(AppPaths.ModsDirectory, "legacy.sml")));
    }
}
