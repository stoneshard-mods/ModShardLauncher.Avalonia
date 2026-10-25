using System.Diagnostics;

namespace ModShardLauncher;

/// <summary>
/// Binary compatibility facade for mods compiled against the WPF build.
/// <para>
/// Historically <c>ModLoader</c> was both the catalog service and a small public API surface
/// (paths, weapon tables, script callbacks, table accessors). The service logic now lives in
/// <see cref="ModCatalog"/>, <see cref="PatchRunner"/> and <see cref="GameTableCodec"/>; this type
/// keeps the static members mods were compiled against, sharing storage with <see cref="MslContext"/>.
/// </para>
/// </summary>
public static class ModLoader
{
    /// <summary>Weapon table rows (legacy public field, kept as a field for binary binding).</summary>
    public static List<string> Weapons = new();

    /// <summary>Equipment description table rows (legacy public field).</summary>
    public static List<string> WeaponDescriptions = new();

    /// <summary>Legacy callback field kept for binary compatibility (the script console was removed).</summary>
    public static Dictionary<string, Action<string>> ScriptCallbacks = new();

    public static string ModPath => AppPaths.ModsDirectory;

    public static string ModSourcesPath => AppPaths.ModSourcesDirectory;

    public static void ShowMessage(string msg) => Trace.Write(msg);

    /// <summary>Legacy name (including the typo) kept so old mods keep binding.</summary>
    public static void Initalize() => GameTableCodec.Initialize();

    public static List<string>? GetTable(string name) => GameTableCodec.GetTable(name);

    public static void SetTable(List<string> table, string name) => GameTableCodec.SetTable(table, name);

    public static void AddMenu(string name, params UIComponent[] components) =>
        MslContext.AddMenu(name, components);

    /// <summary>Legacy entry point; scanning is owned by <see cref="ModCatalog"/>.</summary>
    public static void LoadFiles() =>
        throw new NotSupportedException("Mod scanning is owned by the launcher host; use ModCatalog.Reload().");

    /// <summary>Legacy entry point; patching is owned by <see cref="PatchRunner"/>.</summary>
    public static void PatchMods() =>
        throw new NotSupportedException("Patching is owned by the launcher host; use PatchRunner.Patch().");
}
