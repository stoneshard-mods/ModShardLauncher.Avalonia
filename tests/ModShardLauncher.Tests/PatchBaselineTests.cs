using ModShardLauncher.Mods;
using UndertaleModLib;
using Xunit.Abstractions;

namespace ModShardLauncherTest;

/// <summary>
/// P0 safety net for the patch pipeline. Requires <c>MSL_TEST_DATA</c> pointing at a vanilla
/// <c>data.win</c>; without it every data-dependent test is a no-op so CI stays fast.
/// <para>
/// Workflow:
/// <list type="number">
/// <item><c>MSL_TEST_DATA=...dotnet test</c> — verifies the pipeline is deterministic (two runs
/// must produce byte-identical output and identical semantic fingerprints).</item>
/// <item><c>MSL_TEST_DATA=... MSL_UPDATE_BASELINE=1 dotnet test</c> — records the current output
/// and semantic hashes into <c>Baselines/patch-baselines.json</c>; commit the diff.</item>
/// <item>After every optimisation, run step 1 again: byte-identical output means the change is
/// provably behaviour preserving, a semantic mismatch means the patch content changed.</item>
/// </list>
/// Narrow a slow run with <c>MSL_BASELINE_SCENARIOS=base</c>; skip the second run with
/// <c>MSL_BASELINE_SKIP_DETERMINISM=1</c> (comparison only); force failures with
/// <c>MSL_REQUIRE_BASELINE=1</c>.
/// </para>
/// </summary>
[Collection(GameDataCollection.Name)]
public class PatchBaselineTests
{
    private readonly ITestOutputHelper _output;

    public PatchBaselineTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task PatchPipeline_IsDeterministic_AndMatchesBaseline()
    {
        string? dataPath = PatchBaselineSupport.TryGetDataPath();
        if (dataPath is null)
        {
            _output.WriteLine(PatchBaselineSupport.MissingDataMessage);
            return;
        }

        bool update = PatchBaselineSupport.IsTrue(PatchBaselineSupport.UpdateVariable);
        bool require = PatchBaselineSupport.IsTrue(PatchBaselineSupport.RequireVariable);
        bool skipDeterminism = PatchBaselineSupport.IsTrue(PatchBaselineSupport.SkipDeterminismVariable);

        IReadOnlyList<PatchScenario> scenarios = PatchBaselineSupport.SelectScenarios();
        string baselinePath = PatchBaselineSupport.ResolveBaselinePath();
        PatchBaselineManifest manifest = PatchBaselineManifest.Load(baselinePath);
        string sourceSha = PatchBaselineSupport.Sha256File(dataPath);

        _output.WriteLine($"data.win    : {dataPath} ({sourceSha})");
        _output.WriteLine($"baseline    : {baselinePath}");
        _output.WriteLine($"scenarios   : {string.Join(", ", scenarios.Select(s => s.Name))}");
        _output.WriteLine($"mode        : {(update ? "capture" : "compare")}, determinism {(skipDeterminism ? "off" : "on")}");

        using PatchTestWorkspace workspace = new();
        try
        {
            foreach (PatchScenario scenario in scenarios)
            {
                _output.WriteLine($"--- {scenario.Name} ---");
                ScenarioRun runA = await RunScenarioAsync(dataPath, scenario, workspace, "run-a");
                if (!runA.Executed)
                {
                    ReportSkip(require, scenario.Name, runA.Reason ?? "scenario does not apply to this data.win");
                    continue;
                }

                _output.WriteLine($"  output  : {runA.Sha256}");
                _output.WriteLine($"  semantic: {runA.Semantic}");

                if (!skipDeterminism)
                {
                    ScenarioRun runB = await RunScenarioAsync(dataPath, scenario, workspace, "run-b");
                    _output.WriteLine($"  output2 : {runB.Sha256}");
                    _output.WriteLine($"  semantic2: {runB.Semantic}");
                    Assert.True(
                        string.Equals(runA.Sha256, runB.Sha256, StringComparison.Ordinal),
                        $"Scenario '{scenario.Name}' is not deterministic: run A wrote {runA.Sha256}, run B wrote {runB.Sha256}. " +
                        "The patch pipeline must produce the same bytes for the same input; fix the nondeterminism before " +
                        "comparing against the baseline.");
                    Assert.True(
                        string.Equals(runA.Semantic, runB.Semantic, StringComparison.Ordinal),
                        $"Scenario '{scenario.Name}' is semantically nondeterministic: run A {runA.Semantic}, run B {runB.Semantic}.");
                }

                if (update)
                {
                    manifest.Entries[scenario.Name] = new PatchBaselineEntry
                    {
                        Sha256 = runA.Sha256,
                        Semantic = runA.Semantic,
                    };
                    _output.WriteLine($"  captured baseline entry for '{scenario.Name}'");
                }
                else
                {
                    CompareWithBaseline(manifest, scenario, sourceSha, runA, require);
                }
            }

            if (update)
            {
                manifest.Schema = 1;
                manifest.SourceSha256 = sourceSha;
                manifest.MslVersion = AppInfo.MslVersion;
                manifest.UtmtLibVersion = AppInfo.UtmtLibVersion;
                manifest.Save(baselinePath);
                _output.WriteLine($"Baseline updated: {baselinePath}");
            }
        }
        finally
        {
            LootTableRegistry.ResetLootTables();
        }
    }

    [Fact]
    public void BaselineManifest_IsWellFormed()
    {
        string path = PatchBaselineSupport.ResolveBaselinePath();
        Assert.True(File.Exists(path), $"Baseline manifest is missing: {path} (see PatchBaselineSupport.ResolveBaselinePath)");

        PatchBaselineManifest manifest = PatchBaselineManifest.Load(path);
        Assert.Equal(1, manifest.Schema);
        Assert.NotNull(manifest.Entries);

        foreach ((string name, PatchBaselineEntry entry) in manifest.Entries)
        {
            Assert.Contains(name, PatchBaselineSupport.KnownScenarios);
            Assert.True(
                entry.Sha256.Length is 0 or 64,
                $"Baseline entry '{name}' has a malformed output hash '{entry.Sha256}'.");
            Assert.True(
                entry.Semantic.Length is 0 or 64,
                $"Baseline entry '{name}' has a malformed semantic hash '{entry.Semantic}'.");
        }

        Assert.True(
            manifest.SourceSha256.Length is 0 or 64,
            $"Baseline sourceSha256 is malformed: '{manifest.SourceSha256}'.");
    }

    private async Task<ScenarioRun> RunScenarioAsync(
        string dataPath, PatchScenario scenario, PatchTestWorkspace workspace, string tag)
    {
        GameDataService service = new();
        await service.LoadAsync(dataPath);
        PatchBaselineSupport.AssertVanilla(dataPath);
        scenario.Prepare?.Invoke();

        ModFile[] mods = scenario.CreateMods?.Invoke(workspace) ?? [];
        PatchDiagnostic? diagnostic = new PatchRunner().Patch(mods);
        if (diagnostic is not null)
        {
            if (scenario.BestEffort && PatchBaselineSupport.IsOutdatedAnchor(diagnostic.Exception))
            {
                return ScenarioRun.Unavailable(
                    "the patch pipeline references a GML anchor that no longer exists in this game version: " +
                    diagnostic.Exception.Message.Split('\n')[0]);
            }

            Assert.Fail(diagnostic.Details);
        }

        string output = workspace.File($"{scenario.Name}-{tag}.win");
        await service.SaveAsync(output);

        string sha = PatchBaselineSupport.Sha256File(output);
        string semantic = PatchSemanticFingerprint.Compute(MslContext.Data);

        File.Delete(output);
        return new ScenarioRun(true, sha, semantic, null);
    }

    private void CompareWithBaseline(
        PatchBaselineManifest manifest,
        PatchScenario scenario,
        string sourceSha,
        ScenarioRun run,
        bool require)
    {
        if (manifest.SourceSha256.Length == 0)
        {
            ReportSkip(require, scenario.Name, $"no baseline captured yet (run with {PatchBaselineSupport.UpdateVariable}=1)");
            return;
        }

        if (!string.Equals(manifest.SourceSha256, sourceSha, StringComparison.Ordinal))
        {
            ReportSkip(
                require,
                scenario.Name,
                $"baseline was captured for a different data.win (baseline {manifest.SourceSha256}, current {sourceSha}, " +
                $"msl {manifest.MslVersion}, utmt {manifest.UtmtLibVersion}); recapture it");
            return;
        }

        if (!manifest.Entries.TryGetValue(scenario.Name, out PatchBaselineEntry? entry) || entry.Sha256.Length == 0)
        {
            ReportSkip(require, scenario.Name, $"no baseline entry for this scenario (run with {PatchBaselineSupport.UpdateVariable}=1)");
            return;
        }

        Assert.True(
            string.Equals(entry.Sha256, run.Sha256, StringComparison.Ordinal),
            $"Scenario '{scenario.Name}' output changed: baseline {entry.Sha256}, current {run.Sha256}. " +
            "If the semantic fingerprint still matches, only the serialisation changed; otherwise the patch content changed.");

        Assert.True(
            string.Equals(entry.Semantic, run.Semantic, StringComparison.Ordinal),
            $"Scenario '{scenario.Name}' semantic fingerprint changed: baseline {entry.Semantic}, current {run.Semantic}. " +
            "The patch pipeline now produces different game data; review the change before updating the baseline.");

        _output.WriteLine($"  matches baseline");
    }

    private void ReportSkip(bool require, string scenario, string reason)
    {
        string message = $"Skipped baseline comparison for '{scenario}': {reason}.";
        if (require)
        {
            Assert.Fail(message + $" {PatchBaselineSupport.RequireVariable}=1 forbids skipping.");
        }

        _output.WriteLine("  " + message);
    }
}
