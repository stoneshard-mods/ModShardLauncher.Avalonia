using ModShardLauncher.Mods;
using ModShardLauncher.Runtime.Textures;
using Serilog;

namespace ModShardLauncher;

/// <summary>
/// Applies every enabled mod to the loaded data.win. Replaces <c>ModLoader.PatchMods/PatchFile</c>
/// and keeps the original ordering: log module, mods, loot scripts.
/// </summary>
public sealed class PatchRunner
{
    private readonly TextureInjector _textureInjector;

    public PatchRunner(TextureInjector? textureInjector = null)
    {
        _textureInjector = textureInjector ?? new TextureInjector();
    }

    /// <summary>Patches the loaded data. Returns null on success, or a diagnostic describing the failing mod.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public PatchDiagnostic? Patch(IReadOnlyList<ModFile> mods, CancellationToken cancellationToken = default)
    {
        // A mod can trigger ModCatalog.Reload (missing package) while we are patching, which clears
        // the catalog's live list. Always work on a snapshot so the enumeration cannot break.
        ModFile[] snapshot = mods.ToArray();

        // Cancelling is a host decision, not a patch failure: fail fast before touching the data.
        cancellationToken.ThrowIfCancellationRequested();

        int enabledCount = snapshot.Count(m => m.Enabled);
        Log.Information("Patching {Enabled} enabled mod(s)...", enabledCount);
        if (enabledCount == 0)
        {
            Log.Warning("No mod is enabled; the output will only contain the base patches.");
        }

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using PatchTimings.Scope pipelineTiming = PatchTimings.Measure("pipeline");
        try
        {
            GameLogInjection.InjectLog();
            ApplyMods(snapshot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            LootTableRegistry.InjectLootScripts();
            Log.Information("Patch pipeline completed in {Elapsed} ms", stopwatch.ElapsedMilliseconds);
            return null;
        }
        catch (OperationCanceledException)
        {
            // Do not turn a cancellation into a "mod failed" diagnostic.
            Log.Warning("Patching cancelled after {Elapsed} ms; the loaded data is partially patched", stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            // Failures after the per-mod loop (credits room, menus, loot scripts) have no mod in
            // the Patching state; fall back to a mod-less diagnostic instead of masking the error.
            PatchDiagnostic diagnostic = new(ex, GetFailingMod(snapshot));
            diagnostic.ToLog();
            return diagnostic;
        }
    }

    /// <summary>The mod that was being patched when the pipeline failed, if any.</summary>
    public static ModFile? GetFailingMod(IReadOnlyList<ModFile> mods) =>
        mods.Where(x => x.Enabled).FirstOrDefault(x => x.PatchStatus == PatchStatus.Patching);

    private void ApplyMods(IReadOnlyList<ModFile> mods, CancellationToken cancellationToken)
    {
        MslContext.ResetPatchState();

        foreach (ModFile mod in mods)
        {
            if (!mod.Enabled)
            {
                continue;
            }

            // Mod boundaries are the only safe cancellation points: the data is left partially
            // patched either way, but the host gets a chance to stop before the next mod starts.
            cancellationToken.ThrowIfCancellationRequested();

            if (!mod.Existed)
            {
                Log.Warning("The mod {0} which was located at {1} does not exist anymore.", mod.Name, mod.Path);
                continue;
            }

            mod.PatchStatus = PatchStatus.Patching;
            Log.Information("Applying mod {Name} {Version}", mod.Name, mod.Version);

            // Deliberately wraps the whole mod (textures included): the stage answers "which mod is
            // expensive", while the decompile/compile/table stages break that cost down further.
            using PatchTimings.Scope modTiming = PatchTimings.Measure($"mod:{mod.Name}");

            if (mod.Version != AppInfo.MslVersion)
            {
                Log.Warning("Mod {{{0}}} was built with msl {{{1}}} which is different from the current msl {{{2}}}",
                    mod.Name, mod.Version, AppInfo.MslVersion);
            }

            _textureInjector.Inject(mod);
            mod.instance?.PatchMod();
            if (mod.instance is not null)
            {
                InjectDeclaredWeapons(mod.instance);
            }
            mod.PatchStatus = PatchStatus.Success;
            Log.Information("Mod {Name} patched", mod.Name);
            LogModStatus(mod);
        }

        Log.Information("Creating disclaimer rooms and the in-game mod menu");
        Msl.AddDisclaimerRoom(
            MslContext.Credits.Select(x => x.Item1).ToArray(),
            MslContext.Credits.SelectMany(x => x.Item2).Distinct().ToArray());
        Msl.ChainDisclaimerRooms(MslContext.Disclaimers);
        Msl.CreateMenu(MslContext.Menus);
    }

    /// <summary>
    /// Adds the weapons a mod declared in <see cref="Mod.ModWeapons"/>. Each entry becomes one
    /// weapons-table row plus its equipment-table name/description/gender rows.
    /// </summary>
    private static void InjectDeclaredWeapons(Mod instance)
    {
        foreach (Weapon weapon in instance.ModWeapons)
        {
            Msl.AddWeapon(weapon);
        }
    }

    /// <summary>Writes the per-mod patch result to the log (same message as the WPF build).</summary>
    public static void LogModStatus(ModFile modFile)
    {
        string statusMessage = modFile.PatchStatus switch
        {
            PatchStatus.Patching => "Patching failed",
            PatchStatus.Success => "Patching succeeded",
            _ => "Waiting to be patched",
        };

        Log.Warning("Patching {{{2}}} for {{{0}}} {{{1}}}", modFile.Name, modFile.Version, statusMessage);
    }
}
