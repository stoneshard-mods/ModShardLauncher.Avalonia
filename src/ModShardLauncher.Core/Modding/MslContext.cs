using System.Diagnostics;
using ModShardLauncher.Mods;
using Serilog;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace ModShardLauncher;

/// <summary>
/// Ambient state shared by the static <see cref="Msl"/> API.
/// <para>
/// Historically every helper reached into <c>ModLoader.Data</c> / <c>Main.Instance</c>.
/// The host (<see cref="GameDataService"/> / <see cref="PatchRunner"/>) now owns the lifetime and
/// attaches the game data here before any mod code runs, which keeps the public API static
/// (mod compatibility) while making the state explicit and testable.
/// </para>
/// <para>
/// Storage intentionally aliases the legacy <see cref="DataLoader.data"/> field and
/// <see cref="ModLoader"/> tables: old mods write those members directly, and the API must observe
/// the same objects.
/// </para>
/// </summary>
public static class MslContext
{
    private static bool _attached;

    /// <summary>The data.win currently being patched.</summary>
    /// <exception cref="InvalidOperationException">No game data has been attached yet.</exception>
    public static UndertaleData Data
    {
        get
        {
            if (!_attached)
            {
                throw new InvalidOperationException("No game data (data.win) is currently loaded.");
            }

            return DataLoader.data;
        }
    }

    public static bool HasData => _attached;

    /// <summary>The weapon table loaded by <see cref="GameTableCodec.Initialize"/> (legacy storage).</summary>
    public static List<string> Weapons => ModLoader.Weapons;

    /// <summary>The equipment description table loaded by <see cref="GameTableCodec.Initialize"/> (legacy storage).</summary>
    public static List<string> WeaponDescriptions => ModLoader.WeaponDescriptions;

    internal static List<Menu> Menus = new();
    internal static List<(string, string[])> Credits = new();
    internal static List<(string, UndertaleRoom.GameObject)> Disclaimers = new();

    private static GlobalDecompileContext? _decompilerCache;

    /// <summary>
    /// Decompiler context shared by every decompilation of the currently loaded data file.
    /// <para>
    /// The library caches resolved function argument types (<c>ScriptArgsCache</c>) and anonymous
    /// function names (<c>AnonymousFunctionNameCache</c>) here, and its own documentation says the
    /// context is meant to be reused across runs. Creating a fresh context per call throws those
    /// caches away and makes the decompiler recursively re-analyse the same scripts again, which
    /// is the dominant cost of a patch.
    /// </para>
    /// </summary>
    internal static GlobalDecompileContext DecompilerCache =>
        _decompilerCache ?? throw new InvalidOperationException("No game data (data.win) is currently loaded.");

    /// <summary>
    /// Decompiled text of every code entry read during the current data file's lifetime.
    /// <para>
    /// The decompiler writes the warnings it collected while resolving member variable names as
    /// <c>/* ... */</c> comment blocks at the <b>top</b> of the returned text, and those warnings only
    /// appear while its caches are cold. Reusing one <see cref="GlobalDecompileContext"/> (see
    /// <see cref="DecompilerCache"/>) therefore used to make the same script decompile to two
    /// different texts: the first reader got the warning header, every later one did not. The code
    /// body is identical in both, but the header shifts every line number, which silently moves
    /// anchors for mods that patch by position, and makes a patch depend on mod order.
    /// </para>
    /// <para>
    /// Caching the first result makes a script's text stable for the whole run, which is what the
    /// previous one-context-per-call code produced. Entries are dropped as soon as the code entry is
    /// rewritten (<see cref="InvalidateDecompiledText"/>), so a later read still observes the patch.
    /// </para>
    /// </summary>
    private static readonly Dictionary<UndertaleCode, string> DecompiledText = new();

    /// <summary>Decompiles <paramref name="code"/> once and returns the same text for every later read.</summary>
    internal static string DecompileText(UndertaleCode code)
    {
        if (DecompiledText.TryGetValue(code, out string? cached))
        {
            PatchTimings.Record("decompile-cached", 0);
            Log.Debug("GML decompile {File}: cached, {Length} chars", NameOf(code), cached.Length);
            return cached;
        }

        long start = Stopwatch.GetTimestamp();
        string text = Decompiler.Decompile(code, DecompilerCache);
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        DecompiledText[code] = text;
        PatchTimings.Record($"decompile:{NameOf(code)}", elapsed);
        Log.Debug("GML decompile {File}: {Elapsed} ms, {Length} chars", NameOf(code), elapsed, text.Length);
        return text;
    }

    /// <summary>
    /// Drops the cached text of a code entry that was just rewritten, together with its child
    /// entries (a GML rewrite rebuilds them, so the old ones are dead).
    /// </summary>
    internal static void InvalidateDecompiledText(UndertaleCode? code)
    {
        if (code is null || DecompiledText.Count == 0)
        {
            return;
        }

        DecompiledText.Remove(code);

        if (code.ChildEntries.Count == 0)
        {
            return;
        }

        foreach (UndertaleCode stale in DecompiledText.Keys.Where(x => x.ParentEntry == code).ToArray())
        {
            DecompiledText.Remove(stale);
        }
    }

    private static string NameOf(UndertaleCode code) => code.Name?.Content ?? "<unnamed>";

    /// <summary>Legacy callback storage; kept only for binary compatibility (the script console was removed).</summary>
    public static Dictionary<string, Action<string>> ScriptCallbacks => ModLoader.ScriptCallbacks;

    internal static void Attach(UndertaleData data, string? dataChecksum = null)
    {
        DataLoader.data = data;
        _attached = true;
        ResetDataScopedState();
        _decompilerCache = new GlobalDecompileContext(data, false);

        // UndertaleModTool only sets this flag from its own UI. Without it the GML compiler takes
        // the pre-2.3 path and turns every call into a nameless function stub, which the game then
        // reports as "Unable to find function <name>" when the object is created.
        try
        {
            CompileContext.GMS2_3 = data.IsVersionAtLeast(2, 3);
        }
        catch
        {
            CompileContext.GMS2_3 = true;
        }

        // Optional: replay the decompiler's authoring state for this exact data file instead of
        // rebuilding it (see DecompilerCacheStore). Falls back to the library's own analysis when
        // there is nothing usable, and never throws.
        DecompilerCacheStore.TryApply(dataChecksum, data, _decompilerCache);
    }

    internal static void Detach()
    {
        DataLoader.data = new UndertaleData();
        _attached = false;
        ResetDataScopedState();
    }

    /// <summary>
    /// Drops everything that belongs to one data file: table snapshots, loot registries and the
    /// "index dictionaries already cleared" flag. Keeping them across a load would leak rows and
    /// ids from the previous file into the next one (and make a second patch throw on duplicates).
    /// </summary>
    private static void ResetDataScopedState()
    {
        ModLoader.Weapons = new List<string>();
        ModLoader.WeaponDescriptions = new List<string>();
        LootTableRegistry.ResetLootTables();
        UndertaleIO.IsDictionaryCleared = false;
        _decompilerCache = null;
        DecompiledText.Clear();
        DecompilerCacheStore.Reset();
    }

    internal static void AddCredit(string modNameShort, string[] authors) =>
        Credits.Add((modNameShort, authors));

    internal static void AddDisclaimer(string modNameShort, UndertaleRoom.GameObject overlay) =>
        Disclaimers.Add((modNameShort, overlay));

    internal static void AddMenu(string name, params UIComponent[] components) =>
        Menus.Add(new Menu(name, components));

    /// <summary>
    /// Drops everything one patch run populates. A pipeline that is started twice on the same loaded
    /// data (or a run that is retried in place) must not inherit the previous run's menus, credits or
    /// loot registrations: the loot registry would throw on the duplicate id.
    /// </summary>
    internal static void ResetPatchState()
    {
        Menus = new List<Menu>();
        Credits = new List<(string, string[])>();
        Disclaimers = new List<(string, UndertaleRoom.GameObject)>();
        LootTableRegistry.ResetLootTables();
    }
}
