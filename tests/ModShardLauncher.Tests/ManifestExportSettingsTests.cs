using ModShardLauncher;

namespace ModShardLauncherTest;

/// <summary>B6: manifest export is opt-in and must stay off for existing/new installations.</summary>
public class ManifestExportSettingsTests
{
    [Fact]
    public void ExportManifests_IsOffByDefault()
    {
        Assert.False(new UserSettings().ExportManifests);
        Assert.False(new GameDataService().ExportManifests);
    }

    [Fact]
    public void ExportManifests_CanBeEnabled()
    {
        GameDataService service = new() { ExportManifests = true };
        Assert.True(service.ExportManifests);
    }
}
