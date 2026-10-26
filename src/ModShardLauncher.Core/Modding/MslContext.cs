using ModShardLauncher.Mods;
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

    /// <summary>Legacy callback storage; kept only for binary compatibility (the script console was removed).</summary>
    public static Dictionary<string, Action<string>> ScriptCallbacks => ModLoader.ScriptCallbacks;

    internal static void Attach(UndertaleData data)
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
    }

    internal static void AddCredit(string modNameShort, string[] authors) =>
        Credits.Add((modNameShort, authors));

    internal static void AddDisclaimer(string modNameShort, UndertaleRoom.GameObject overlay) =>
        Disclaimers.Add((modNameShort, overlay));

    internal static void AddMenu(string name, params UIComponent[] components) =>
        Menus.Add(new Menu(name, components));

    internal static void ResetPatchState()
    {
        Menus = new List<Menu>();
        Credits = new List<(string, string[])>();
        Disclaimers = new List<(string, UndertaleRoom.GameObject)>();
    }
}
