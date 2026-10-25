using ModShardLauncher;

namespace ModShardLauncherTest;

/// <summary>
/// B5: the persisted enabled list must be a snapshot of the current checkboxes, not an
/// append-only log. This covers the de-duplication used when saving and applying it.
/// </summary>
public class EnabledModsTests
{
    [Fact]
    public void GetEnabledModNames_ReturnsDistinctEnabledModNames()
    {
        List<ModFile> mods =
        [
            new() { Name = "A", Enabled = true },
            new() { Name = "B", Enabled = false },
            new() { Name = "A", Enabled = true },
            new() { Name = "C", Enabled = true },
        ];

        List<string> names = ModCatalog.GetEnabledModNames(mods);

        Assert.Equal(["A", "C"], names);
    }
}
