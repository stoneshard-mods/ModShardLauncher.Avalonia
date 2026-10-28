using ModShardLauncher;
using ModShardLauncher.Mods;
using Xunit.Abstractions;

namespace ModShardLauncherTest;

/// <summary>
/// Opt-in regression tests for the batched table write-back (<see cref="GameTableCodec"/> /
/// internal <c>TableDraftStore</c>). Requires <c>MSL_TEST_DATA</c> pointing at a vanilla
/// <c>data.win</c>; without it every test is a no-op so CI stays fast.
/// <para>
/// The table scripts are the two biggest GML functions in the game and every write used to
/// recompile the whole script. These tests pin the two properties the batching must keep:
/// one compile per table per patch run, and a read of the script observing the queued rows.
/// </para>
/// </summary>
[Collection(GameDataCollection.Name)]
public class TableBatchingTests
{
    private const string WeaponsTable = "gml_GlobalScript_table_weapons";
    private const string EquipmentTable = "gml_GlobalScript_table_equipment";
    private const string ProbePrefix = "msl_batch_weapon_";

    private readonly ITestOutputHelper _output;

    public TableBatchingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AddingWeapons_CompilesEachTableOnce_AndWritesEveryRow()
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

        PatchTimings.Reset();
        ModFile mod = workspace.CreateMod("BatchWeapons", new BatchWeaponMod(5));
        PatchDiagnostic? diagnostic = new PatchRunner().Patch([mod]);
        Assert.Null(diagnostic);

        // One compile per table, however many rows the API appended.
        Assert.Equal(1, CountStage($"table:{WeaponsTable}"));
        Assert.Equal(1, CountStage($"table:{EquipmentTable}"));

        List<string> weapons = Assert.IsType<List<string>>(GameTableCodec.GetTable(WeaponsTable));
        Assert.Equal(5, weapons.Count(row => row.StartsWith(ProbePrefix, StringComparison.Ordinal)));

        List<string> equipment = Assert.IsType<List<string>>(GameTableCodec.GetTable(EquipmentTable));
        Assert.Equal(15, equipment.Count(row => row.StartsWith(ProbePrefix, StringComparison.Ordinal)));

        // The weapons table must still have its header first and the new rows at the end.
        Assert.StartsWith("name;", weapons[0], StringComparison.Ordinal);
        Assert.All(weapons.Skip(1).Where(row => row.StartsWith(ProbePrefix, StringComparison.Ordinal)),
            row => Assert.StartsWith(ProbePrefix, row, StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueuedTableWrite_IsVisibleToTheScriptReader()
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

        PatchTimings.Reset();
        Msl.AddWeapon(new Weapon { Name = "msl_flush_probe", ID = "msl_flush_probe" });

        // Nothing is compiled until something asks for the script.
        Assert.Equal(0, CountStage($"table:{WeaponsTable}"));
        Assert.Equal(0, CountStage($"table:{EquipmentTable}"));

        string weaponsText = Msl.GetStringGMLFromFile(WeaponsTable);
        Assert.Contains("msl_flush_probe", weaponsText, StringComparison.Ordinal);
        Assert.Equal(1, CountStage($"table:{WeaponsTable}"));

        // Reading one table must not compile the other one: they flush independently.
        Assert.Equal(0, CountStage($"table:{EquipmentTable}"));
        string equipmentText = Msl.GetStringGMLFromFile(EquipmentTable);
        Assert.Contains("msl_flush_probe", equipmentText, StringComparison.Ordinal);
        Assert.Equal(1, CountStage($"table:{EquipmentTable}"));

        // Reading the same text twice must not recompile (the decompiled text is cached again).
        _ = Msl.GetStringGMLFromFile(WeaponsTable);
        Assert.Equal(1, CountStage($"table:{WeaponsTable}"));
    }

    private static int CountStage(string stage) =>
        PatchTimings.Snapshot().Count(x => string.Equals(x.Stage, stage, StringComparison.Ordinal));

    private sealed class BatchWeaponMod : Mod
    {
        public override string Name => "BatchWeapons";
        public override string Version => AppInfo.MslVersion;

        public BatchWeaponMod(int count)
        {
            for (int i = 0; i < count; i++)
            {
                ModWeapons.Add(new Weapon
                {
                    Name = $"{ProbePrefix}{i:D2}",
                    ID = $"{ProbePrefix}{i:D2}",
                });
            }
        }
    }
}
