using System.Reflection;
using System.Runtime.Loader;

namespace ModShardLauncher;

/// <summary>
/// Load context for packed <c>.sml</c> mod assemblies.
/// <para>
/// Mods are compiled against the launcher and reference <c>ModShardLauncher</c> with the version
/// that existed when they were built (historically 1.0.0.0). Loading the assembly from bytes makes
/// the runtime resolve that reference by exact name, which fails when the launcher version differs.
/// Binding every "ModShardLauncher" request to the already loaded API assembly keeps old packages
/// working, while still allowing recent ones; every other dependency is resolved from the
/// application folder or the default context.
/// </para>
/// </summary>
public sealed class ModAssemblyLoadContext : AssemblyLoadContext
{
    public static ModAssemblyLoadContext Instance { get; } = new();

    private ModAssemblyLoadContext()
        : base("ModShardLauncher.Mods", isCollectible: false)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // The launcher contract (and anything else already loaded) is shared with the host,
        // so the Mod base type keeps a single identity across all mods.
        foreach (Assembly assembly in Default.Assemblies)
        {
            if (string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
            {
                return assembly;
            }
        }

        if (assemblyName.Name is null)
        {
            return null;
        }

        // Mods may ship dependencies next to the launcher (for example UndertaleModTool).
        string candidate = Path.Combine(AppContext.BaseDirectory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }

    /// <summary>Loads a mod assembly from the raw bytes stored in the .sml container.</summary>
    public Assembly LoadMod(byte[] assemblyBytes)
    {
        using MemoryStream stream = new(assemblyBytes, writable: false);
        return LoadFromStream(stream);
    }
}
