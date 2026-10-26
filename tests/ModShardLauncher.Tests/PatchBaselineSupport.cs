using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModShardLauncher.Mods;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace ModShardLauncherTest;

/// <summary>
/// Serialises every test that touches the process-wide patch state
/// (<see cref="MslContext"/>, <see cref="ModLoader"/>, <see cref="LootTableRegistry"/>).
/// </summary>
[CollectionDefinition(GameDataCollection.Name, DisableParallelization = true)]
public sealed class GameDataCollection
{
    public const string Name = "GameData";
}

/// <summary>One reproducible patch run: optional prelude, optional mods, then <see cref="PatchRunner"/>.</summary>
/// <param name="BestEffort">
/// When true, a failure caused by an outdated GML anchor is reported as "scenario unavailable"
/// instead of failing the test. Used by scenarios that depend on game-version specific code.
/// </param>
internal sealed record PatchScenario(
    string Name,
    Action? Prepare = null,
    Func<PatchTestWorkspace, ModFile[]>? CreateMods = null,
    bool BestEffort = false);

/// <summary>Outcome of one scenario run; <see cref="Executed"/> is false when it does not apply.</summary>
internal sealed record ScenarioRun(bool Executed, string Sha256, string Semantic, string? Reason)
{
    public static ScenarioRun Unavailable(string reason) => new(false, string.Empty, string.Empty, reason);
}

/// <summary>Temporary directory for fake mod packages and patched output files.</summary>
internal sealed class PatchTestWorkspace : IDisposable
{
    public PatchTestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "msl-patch-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name) => Path.Combine(Root, name);

    /// <summary>
    /// Creates a mod entry that the pipeline can run. The package file is only a placeholder:
    /// <see cref="ModFile.Files"/> stays empty, so texture injection sees no sources.
    /// </summary>
    public ModFile CreateMod(string name, Mod instance)
    {
        string path = File(name + ".sml");
        System.IO.File.WriteAllBytes(path, [0]);

        return new ModFile
        {
            Name = name,
            Version = AppInfo.MslVersion,
            Path = path,
            Enabled = true,
            instance = instance,
        };
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best effort only: a leaked temp directory must never fail a test.
        }
    }
}

/// <summary>Patch scenarios that are cheap to reproduce from a vanilla <c>data.win</c>.</summary>
internal static class PatchBaselineSupport
{
    public const string DataPathVariable = "MSL_TEST_DATA";
    public const string UpdateVariable = "MSL_UPDATE_BASELINE";
    public const string RequireVariable = "MSL_REQUIRE_BASELINE";
    public const string ScenariosVariable = "MSL_BASELINE_SCENARIOS";
    public const string SkipDeterminismVariable = "MSL_BASELINE_SKIP_DETERMINISM";
    public const string BaselineFileVariable = "MSL_BASELINE_FILE";

    public static readonly string[] KnownScenarios = ["base", "loot", "weapons"];

    /// <summary>Marker the base patch writes into the game loader; one occurrence per successful injection.</summary>
    public const string LogHookMarker = "global._msl_log = instance_create_depth";

    public const string LoaderCreateEvent = "gml_Object_o_gameLoader_Create_0";

    public static string MissingDataMessage =>
        $"Set {DataPathVariable} to a vanilla data.win to run this test; it is a no-op otherwise.";

    /// <summary>The vanilla data.win used by the opt-in patch tests, or null when not configured.</summary>
    public static string? TryGetDataPath()
    {
        string? path = Environment.GetEnvironmentVariable(DataPathVariable);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    public static bool IsTrue(string variable) =>
        string.Equals(Environment.GetEnvironmentVariable(variable), "1", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fails early with an actionable message when the configured file is already patched. Steam
    /// installs often keep a patched <c>data.win</c> next to the vanilla <c>original.win</c>.
    /// </summary>
    public static void AssertVanilla(string dataPath)
    {
        bool alreadyPatched = MslContext.Data.GameObjects.Any(o => o.Name?.Content == "o_msl_timer");
        Assert.False(
            alreadyPatched,
            $"{dataPath} already contains MSL patches (object o_msl_timer exists), so it is not a vanilla input. " +
            $"Point {DataPathVariable} at a clean vanilla data.win (for a Steam install that is usually original.win).");
    }

    /// <summary>
    /// Scenarios to run, narrowed by <c>MSL_BASELINE_SCENARIOS</c> (comma separated). Order follows
    /// <see cref="KnownScenarios"/> so the baseline file stays stable.
    /// </summary>
    public static IReadOnlyList<PatchScenario> SelectScenarios()
    {
        string? selection = Environment.GetEnvironmentVariable(ScenariosVariable);
        HashSet<string>? requested = null;
        if (!string.IsNullOrWhiteSpace(selection))
        {
            requested = new HashSet<string>(
                selection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

            string unknown = string.Join(", ", requested.Except(KnownScenarios, StringComparer.OrdinalIgnoreCase));
            if (unknown.Length > 0)
            {
                throw new ArgumentException(
                    $"{ScenariosVariable} contains unknown scenario(s): {unknown}. Known: {string.Join(", ", KnownScenarios)}");
            }
        }

        List<PatchScenario> scenarios = new();
        foreach (PatchScenario scenario in All())
        {
            if (requested is null || requested.Contains(scenario.Name))
            {
                scenarios.Add(scenario);
            }
        }

        return scenarios;
    }

    private static IEnumerable<PatchScenario> All()
    {
        // Nothing but the built-in base patches (logger module, disclaimer room, menu).
        yield return new PatchScenario("base");

        // Exercises LootTableRegistry.InjectLootScripts (5 GML functions + 2 script edits).
        // Best effort: the injection anchors on a vanilla "script_execute" line that is missing in
        // newer game data, in which case the scenario reports itself as unavailable instead of
        // failing every baseline comparison.
        yield return new PatchScenario("loot", Prepare: RegisterLootTables, BestEffort: true);

        // Exercises Msl.AddWeapon -> weapons table + equipment table writes.
        yield return new PatchScenario("weapons", CreateMods: workspace =>
            [workspace.CreateMod("BaselineWeapon", new BaselineWeaponMod())]);
    }

    private static void RegisterLootTables()
    {
        ItemsTable items = new(["msl_baseline_item"], [1], [1]);
        Msl.AddLootTable(
            "msl_baseline_loot",
            items,
            randomLootMin: 1,
            randomLootMax: 1,
            emptyWeight: 0,
            new RandomItemsTable([1], items));
        Msl.AddReferenceTable("o_msl_baseline_loot_source", "msl_baseline_loot");
    }

    public static string Sha256File(string path)
    {
        using FileStream stream = System.IO.File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string Sha256Text(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// True when the pipeline failed because a hard-coded anchor line no longer exists in the
    /// game version being patched (see <see cref="Msl.MatchFrom(System.Collections.Generic.IEnumerable{string}, string)"/>).
    /// </summary>
    public static bool IsOutdatedAnchor(Exception exception) =>
        exception is InvalidOperationException && exception.Message.Contains("No matching lines found", StringComparison.Ordinal);

    public static int CountOccurrences(string text, string marker)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }

    /// <summary>
    /// Baseline file used to detect unintended changes in patch output. Prefers the repository copy
    /// so <c>MSL_UPDATE_BASELINE=1</c> produces a reviewable diff instead of writing into bin/.
    /// </summary>
    public static string ResolveBaselinePath()
    {
        string? overridePath = Environment.GetEnvironmentVariable(BaselineFileVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "tests", "ModShardLauncher.Tests", "Baselines", "patch-baselines.json");
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Baselines", "patch-baselines.json");
    }
}

/// <summary>
/// Stable digest of everything a patch run can change, independent of the binary layout of data.win.
/// Two differently serialised files must produce the same fingerprint when they mean the same thing.
/// </summary>
internal static class PatchSemanticFingerprint
{
    public static string Compute(UndertaleData data)
    {
        StringBuilder builder = new(1 << 20);

        builder.Append("code=").Append(data.Code.Count).Append('\n');
        foreach (UndertaleCode code in data.Code)
        {
            builder.Append(code.Name?.Content ?? "<null>")
                .Append('|').Append(code.Instructions?.Count ?? -1)
                .Append('|').Append(code.LocalsCount)
                .Append('\n');
        }

        builder.Append("codeLocals=").Append(data.CodeLocals.Count).Append('\n');
        builder.Append("scripts=").Append(data.Scripts.Count).Append('\n');
        foreach (UndertaleScript script in data.Scripts)
        {
            builder.Append(script.Name?.Content ?? "<null>").Append('\n');
        }

        builder.Append("objects=").Append(data.GameObjects.Count).Append('\n');
        foreach (UndertaleGameObject gameObject in data.GameObjects)
        {
            int events = 0;
            int actions = 0;
            foreach (IList<UndertaleGameObject.Event> slot in gameObject.Events)
            {
                events += slot.Count;
                foreach (UndertaleGameObject.Event entry in slot)
                {
                    actions += entry.Actions.Count;
                }
            }

            builder.Append(gameObject.Name?.Content ?? "<null>")
                .Append('|').Append(events)
                .Append('|').Append(actions)
                .Append('\n');
        }

        builder.Append("rooms=").Append(data.Rooms.Count).Append('\n');
        foreach (UndertaleRoom room in data.Rooms)
        {
            builder.Append(room.Name?.Content ?? "<null>")
                .Append('|').Append(room.Layers.Count)
                .Append('|').Append(room.GameObjects.Count)
                .Append('\n');
        }

        builder.Append("sprites=").Append(data.Sprites.Count).Append('\n');
        foreach (UndertaleSprite sprite in data.Sprites)
        {
            builder.Append(sprite.Name?.Content ?? "<null>")
                .Append('|').Append(sprite.Textures.Count)
                .Append('|').Append(sprite.CollisionMasks.Count)
                .Append('\n');
        }

        builder.Append("embeddedTextures=").Append(data.EmbeddedTextures.Count).Append('\n');
        foreach (UndertaleEmbeddedTexture texture in data.EmbeddedTextures)
        {
            builder.Append(texture.Name?.Content ?? "<null>").Append('\n');
        }

        builder.Append("texturePageItems=").Append(data.TexturePageItems.Count).Append('\n');
        foreach (UndertaleTexturePageItem pageItem in data.TexturePageItems)
        {
            builder.Append(pageItem.Name?.Content ?? "<null>").Append('\n');
        }

        builder.Append("variables=").Append(data.Variables.Count).Append('\n');
        builder.Append("strings=").Append(data.Strings.Count()).Append('\n');

        AppendTable(builder, "table_weapons", "gml_GlobalScript_table_weapons");
        AppendTable(builder, "table_equipment", "gml_GlobalScript_table_equipment");

        return PatchBaselineSupport.Sha256Text(builder.ToString());
    }

    private static void AppendTable(StringBuilder builder, string label, string codeName)
    {
        List<string>? rows;
        try
        {
            rows = GameTableCodec.GetTable(codeName);
        }
        catch (Exception ex)
        {
            builder.Append(label).Append("=unavailable:").Append(ex.GetType().Name).Append('\n');
            return;
        }

        if (rows is null)
        {
            builder.Append(label).Append("=null\n");
            return;
        }

        builder.Append(label)
            .Append('=').Append(rows.Count)
            .Append('|').Append(PatchBaselineSupport.Sha256Text(string.Join("\n", rows)))
            .Append('\n');
    }
}

/// <summary>Baseline manifest: one output hash plus one semantic fingerprint per scenario.</summary>
internal sealed class PatchBaselineManifest
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public int Schema { get; set; } = 1;

    public string Note { get; set; } =
        "Generated by PatchBaselineTests with MSL_UPDATE_BASELINE=1 against a vanilla data.win. " +
        "Compare with the semantic fingerprint to tell a formatting change from a behaviour change.";

    /// <summary>SHA-256 of the input data.win, so a game update invalidates the baseline explicitly.</summary>
    public string SourceSha256 { get; set; } = string.Empty;

    public string MslVersion { get; set; } = string.Empty;

    public string UtmtLibVersion { get; set; } = string.Empty;

    public SortedDictionary<string, PatchBaselineEntry> Entries { get; set; } =
        new(StringComparer.Ordinal);

    public static PatchBaselineManifest Load(string path)
    {
        if (!File.Exists(path))
        {
            return new PatchBaselineManifest();
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PatchBaselineManifest>(json, Options) ?? new PatchBaselineManifest();
    }

    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, Options) + Environment.NewLine);
    }
}

internal sealed class PatchBaselineEntry
{
    /// <summary>SHA-256 of the patched data.win written by <see cref="GameDataService.SaveAsync"/>.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>SHA-256 of <see cref="PatchSemanticFingerprint"/>; catches intentional data changes.</summary>
    public string Semantic { get; set; } = string.Empty;
}

/// <summary>Mods used by the baseline scenarios; they only touch the public modding API.</summary>
internal sealed class BaselineNoOpMod : Mod
{
    public override string Name => "BaselineNoOp";
}

internal sealed class BaselineThrowingMod : Mod
{
    public override string Name => "BaselineThrowing";

    public override void PatchMod() => throw new InvalidOperationException("intentional baseline failure");
}

internal sealed class BaselineTrackingMod : Mod
{
    public override string Name => "BaselineTracking";

    public bool Patched { get; private set; }

    public override void PatchMod() => Patched = true;
}

/// <summary>Declares one weapon so the pipeline writes both the weapons and the equipment table.</summary>
internal sealed class BaselineWeaponMod : Mod
{
    public override string Name => "BaselineWeapon";

    public BaselineWeaponMod()
    {
        ModWeapons.Add(new Weapon
        {
            Name = "msl_baseline_probe",
            ID = "msl_baseline_probe",
        });
    }
}
