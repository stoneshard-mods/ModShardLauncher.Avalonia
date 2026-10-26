using ModShardLauncher.Mods;
using Xunit.Abstractions;

namespace ModShardLauncherTest;

/// <summary>
/// Locks down how the patch pipeline behaves today when it is run twice or when a mod throws.
/// These tests describe known defects on purpose: the assertions must be updated together with
/// the fix (P1), never deleted silently. Requires <c>MSL_TEST_DATA</c>.
/// </summary>
[Collection(GameDataCollection.Name)]
public class PatchFailureCharacterizationTests
{
    private readonly ITestOutputHelper _output;

    public PatchFailureCharacterizationTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Known defect (P1): patching the same UndertaleData twice injects the log hook a second time
    /// and only then fails, because the objects/events already exist. The failure is therefore not
    /// side-effect free, which is why the GUI reloads the vanilla file after every attempt.
    /// </summary>
    [Fact]
    public async Task SecondPatchOnTheSameData_InjectsTheLogHookAgainBeforeFailing()
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
        Assert.True(
            second is not null,
            "the second patch is expected to fail while adding events that already exist; " +
            "if this now succeeds, idempotency was implemented and this test should assert equality instead");

        string loaderAfterSecond = Msl.GetStringGMLFromFile(PatchBaselineSupport.LoaderCreateEvent);
        Assert.Equal(2, PatchBaselineSupport.CountOccurrences(loaderAfterSecond, PatchBaselineSupport.LogHookMarker));
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
    /// <see cref="LootTableRegistry"/> is not reset by <see cref="PatchRunner"/>, so a second patch
    /// on the same data would throw on the duplicate id. Pinned here because the P1 fix resets it.
    /// </summary>
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
