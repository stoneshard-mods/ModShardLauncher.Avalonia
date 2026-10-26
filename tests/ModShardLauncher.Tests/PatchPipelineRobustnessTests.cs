using ModShardLauncher.Mods;
using Xunit.Abstractions;

namespace ModShardLauncherTest;

/// <summary>
/// P1 hardening of the patch pipeline: snapshot enumeration, cancellation, atomic save and
/// per-data-file state. Tests that need a vanilla <c>data.win</c> read <c>MSL_TEST_DATA</c> and
/// become no-ops otherwise.
/// </summary>
[Collection(GameDataCollection.Name)]
public class PatchPipelineRobustnessTests
{
    private readonly ITestOutputHelper _output;

    public PatchPipelineRobustnessTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Patch_WithAlreadyCancelledToken_ThrowsInsteadOfReturningADiagnostic()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        List<ModFile> mods =
        [
            new ModFile { Name = "never-runs", Enabled = true, Path = "unused.sml" },
        ];

        Assert.Throws<OperationCanceledException>(() => new PatchRunner().Patch(mods, cancellation.Token));
        Assert.Equal(PatchStatus.None, mods[0].PatchStatus);
    }

    /// <summary>
    /// A mod can make <see cref="ModCatalog"/> reload while the pipeline is enumerating its list
    /// (that is what <see cref="ModFile.GetFile"/> does when a package disappeared). The pipeline
    /// must work on a snapshot instead of throwing "Collection was modified".
    /// </summary>
    [Fact]
    public async Task Patch_WorksOnASnapshot_WhenTheModListIsMutatedDuringPatching()
    {
        string? dataPath = PatchBaselineSupport.TryGetDataPath();
        if (dataPath is null)
        {
            _output.WriteLine(PatchBaselineSupport.MissingDataMessage);
            return;
        }

        using PatchTestWorkspace workspace = new();
        GameDataService service = new();
        await service.LoadAsync(dataPath);
        PatchBaselineSupport.AssertVanilla(dataPath);

        BaselineTrackingMod tracking = new();
        ModFile tracked = workspace.CreateMod("Tracking", tracking);

        List<ModFile> mods = new();
        mods.Add(workspace.CreateMod("NoOp", new BaselineNoOpMod()));
        mods.Add(workspace.CreateMod("Removing", new RemovingMod(mods, tracked)));
        mods.Add(tracked);

        PatchDiagnostic? diagnostic = new PatchRunner().Patch(mods);

        Assert.True(diagnostic is null, diagnostic?.Details ?? "the pipeline must tolerate a mutating catalog");
        Assert.DoesNotContain(tracked, mods);
        Assert.True(tracking.Patched, "the snapshot keeps the mods the run started with");
    }

    [Fact]
    public async Task SaveAsync_ReplacesAnExistingTarget_AndLeavesNoTempFileBehind()
    {
        string? dataPath = PatchBaselineSupport.TryGetDataPath();
        if (dataPath is null)
        {
            _output.WriteLine(PatchBaselineSupport.MissingDataMessage);
            return;
        }

        using PatchTestWorkspace workspace = new();
        GameDataService service = new();
        await service.LoadAsync(dataPath);

        string existing = workspace.File("existing.win");
        File.WriteAllBytes(existing, [1, 2, 3, 4]);

        await service.SaveAsync(existing);
        Assert.Equal(existing, service.SavedDataPath);
        Assert.True(new FileInfo(existing).Length > 1_000_000, "the placeholder content must be replaced");
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp"));

        string missing = workspace.File("missing.win");
        await service.SaveAsync(missing);
        Assert.True(File.Exists(missing));
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_WhenTheTargetDirectoryIsMissing_KeepsSavedDataPathEmpty()
    {
        GameDataService service = new();
        string badPath = Path.Combine(Path.GetTempPath(), $"msl-missing-{Guid.NewGuid():N}", "out.win");

        await Assert.ThrowsAsync<AggregateException>(() => service.SaveAsync(badPath));

        Assert.Equal(string.Empty, service.SavedDataPath);
    }

    /// <summary>
    /// Cancelling before the save starts must leave no output and no temp file behind, and must not
    /// report a saved path.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WithACancelledToken_LeavesNothingBehind()
    {
        string? dataPath = PatchBaselineSupport.TryGetDataPath();
        if (dataPath is null)
        {
            _output.WriteLine(PatchBaselineSupport.MissingDataMessage);
            return;
        }

        using PatchTestWorkspace workspace = new();
        GameDataService service = new();
        await service.LoadAsync(dataPath);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        string target = workspace.File("cancelled.win");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(target, cancellation.Token));

        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(workspace.Root, "*.tmp"));
        Assert.Equal(string.Empty, service.SavedDataPath);
    }

    /// <summary>
    /// Table snapshots and loot registrations belong to one data file. Loading another file must
    /// not observe the rows/ids of the previous one (a second patch would throw on duplicates).
    /// </summary>
    [Fact]
    public async Task LoadingNewData_DropsTheTableSnapshotsAndLootRegistrationsOfThePreviousFile()
    {
        string? dataPath = PatchBaselineSupport.TryGetDataPath();
        if (dataPath is null)
        {
            _output.WriteLine(PatchBaselineSupport.MissingDataMessage);
            return;
        }

        using PatchTestWorkspace workspace = new();
        GameDataService service = new();
        await service.LoadAsync(dataPath);

        Assert.True(ModLoader.Weapons.Count > 1, "the weapons table must be loaded from the data file");
        ModLoader.Weapons = ["stale_marker"];

        ItemsTable items = new(["item"], [1], [1]);
        Msl.AddLootTable("leak_marker", items, randomLootMin: 1, randomLootMax: 1, emptyWeight: 0, new RandomItemsTable([1], items));

        string firstLoot = workspace.File("loot-before");
        Directory.CreateDirectory(firstLoot);
        LootTableRegistry.SaveLootTables(firstLoot);
        Assert.True(File.Exists(Path.Combine(firstLoot, "loot_table.json")), "the loot registry must contain the test table");

        await service.LoadAsync(dataPath);

        Assert.DoesNotContain("stale_marker", ModLoader.Weapons);
        Assert.True(ModLoader.Weapons.Count > 1, "the new data file repopulates the weapons snapshot");

        string secondLoot = workspace.File("loot-after");
        Directory.CreateDirectory(secondLoot);
        LootTableRegistry.SaveLootTables(secondLoot);
        Assert.False(
            File.Exists(Path.Combine(secondLoot, "loot_table.json")),
            "the loot registry must be empty after loading a new data file");
    }

    /// <summary>Removes another mod from the catalog list while the pipeline is running.</summary>
    private sealed class RemovingMod(List<ModFile> mods, ModFile victim) : Mod
    {
        public override string Name => "Removing";

        public override void PatchMod() => mods.Remove(victim);
    }
}
