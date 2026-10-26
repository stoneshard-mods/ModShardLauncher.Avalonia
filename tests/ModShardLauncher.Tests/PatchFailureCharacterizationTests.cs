using ModShardLauncher.Mods;
using Xunit.Abstractions;

namespace ModShardLauncherTest;

/// <summary>
/// Locks down how the patch pipeline behaves on inputs it must refuse.
/// <para>
/// These started as "known defect" tests: patching the same data twice used to inject the log hook
/// again and only then fail on the existing event, leaving a duplicate behind. The pipeline now
/// refuses an already patched file before writing anything, so the assertions below describe the
/// fixed behaviour (and the duplicate-injection defect stays pinned by the marker count).
/// </para>
/// </summary>
[Collection(GameDataCollection.Name)]
public class PatchFailureCharacterizationTests
{
    private readonly ITestOutputHelper _output;

    public PatchFailureCharacterizationTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Patching an already patched data file must fail <b>before</b> touching it: no second log hook,
    /// and an actionable message instead of "an event with this subtype already exists".
    /// </summary>
    [Fact]
    public async Task SecondPatchOnTheSameData_FailsBeforeWritingAnything()
    {
        string? dataPath = PatchBaselineSupport.TryGetDataPath();
        if (dataPath is null)
        {
            _output.WriteLine(PatchBaselineSupport.MissingDataMessage);
            return;
        }

        GameDataService service = new();
        await service.LoadAsync(dataPath);
        PatchBaselineSupport.AssertVanilla(dataPath);

        PatchDiagnostic? first = new PatchRunner().Patch(Array.Empty<ModFile>());
        Assert.True(first is null, first?.Details ?? "the first patch must succeed");

        string loaderAfterFirst = Msl.GetStringGMLFromFile(PatchBaselineSupport.LoaderCreateEvent);
        Assert.Equal(1, PatchBaselineSupport.CountOccurrences(loaderAfterFirst, PatchBaselineSupport.LogHookMarker));

        PatchDiagnostic? second = new PatchRunner().Patch(Array.Empty<ModFile>());
        Assert.NotNull(second);
        Assert.IsType<InvalidOperationException>(second!.Exception);
        Assert.Contains("already contains", second.Exception!.Message, StringComparison.Ordinal);
        Assert.Contains("already patched", second.Exception.Message, StringComparison.Ordinal);

        // The failure must be side-effect free: the hook must not have been injected a second time.
        string loaderAfterSecond = Msl.GetStringGMLFromFile(PatchBaselineSupport.LoaderCreateEvent);
        Assert.Equal(1, PatchBaselineSupport.CountOccurrences(loaderAfterSecond, PatchBaselineSupport.LogHookMarker));
    }

    /// <summary>
    /// Known behaviour: the first failing mod aborts the whole pipeline, later mods never run, and
    /// the failing mod stays in <see cref="PatchStatus.Patching"/> so the host can report it.
    /// </summary>
    [Fact]
    public async Task FailingMod_AbortsThePipeline_AndLeavesTheRemainingModsUntouched()
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

        ModFile successful = workspace.CreateMod("BaselineNoOp", new BaselineNoOpMod());
        ModFile throwing = workspace.CreateMod("BaselineThrowing", new BaselineThrowingMod());
        BaselineTrackingMod tracked = new();
        ModFile neverRun = workspace.CreateMod("BaselineTracking", tracked);

        PatchDiagnostic? diagnostic = new PatchRunner().Patch([successful, throwing, neverRun]);

        Assert.True(diagnostic is not null, "a throwing mod must stop the pipeline and produce a diagnostic");
        Assert.Equal("BaselineThrowing", diagnostic!.ModName);
        Assert.IsType<InvalidOperationException>(diagnostic.Exception);

        Assert.Equal(PatchStatus.Success, successful.PatchStatus);
        Assert.Equal(PatchStatus.Patching, throwing.PatchStatus);
        Assert.Equal(PatchStatus.None, neverRun.PatchStatus);
        Assert.False(tracked.Patched, "mods after the failing one must not run");
    }

    /// <summary>
    /// <see cref="LootTableRegistry"/> is per patch run: the pipeline clears it before running the
    /// mods, so a retry in place cannot throw on the duplicate id.
    /// </summary>
    [Fact]
    public void ResetPatchState_ClearsTheLootRegistry()
    {
        LootTableRegistry.ResetLootTables();
        try
        {
            ItemsTable items = new(["item"], [1], [1]);
            RandomItemsTable random = new([1], items);
            Msl.AddLootTable("reset_marker", items, randomLootMin: 1, randomLootMax: 1, emptyWeight: 0, random);
            Assert.Single(LootTableRegistry.LootTables);

            MslContext.ResetPatchState();

            Assert.Empty(LootTableRegistry.LootTables);
            Assert.Empty(LootTableRegistry.ReferenceTables);
        }
        finally
        {
            LootTableRegistry.ResetLootTables();
        }
    }

    /// <summary>A duplicate id inside one run is still a programming error and must throw.</summary>
    [Fact]
    public void LootTableRegistry_RejectsDuplicateIds()
    {
        LootTableRegistry.ResetLootTables();
        try
        {
            ItemsTable items = new(["item"], [1], [1]);
            RandomItemsTable random = new([1], items);

            Msl.AddLootTable("duplicate", items, randomLootMin: 1, randomLootMax: 1, emptyWeight: 0, random);
            Assert.Throws<ArgumentException>(() =>
                Msl.AddLootTable("duplicate", items, randomLootMin: 1, randomLootMax: 1, emptyWeight: 0, random));
        }
        finally
        {
            LootTableRegistry.ResetLootTables();
        }
    }
}
