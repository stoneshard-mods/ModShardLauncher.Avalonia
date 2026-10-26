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
/// <para>
/// The runtime refuses to load two assemblies with the same identity into the <b>same</b> context
/// ("Assembly with same name is already loaded"), so recompiling a mod and reloading it used to make
/// the mod disappear from the list with a <see cref="FileLoadException"/>. Each package build
/// therefore gets its own context through <see cref="CreateForPackage"/>; the shared
/// <see cref="Instance"/> only serves unpacked callers.
/// </para>
/// </summary>
public sealed class ModAssemblyLoadContext : AssemblyLoadContext
{
    /// <summary>Shared context; kept for callers that do not need per-package isolation.</summary>
    public static ModAssemblyLoadContext Instance { get; } = new("ModShardLauncher.Mods");

    private ModAssemblyLoadContext(string name)
        : base(name, isCollectible: false)
    {
    }

    /// <summary>
    /// Creates an isolated context for one package build. A new build of the same mod must not go
    /// through the context of the previous build: its assembly identity is unchanged, so the runtime
    /// would reject it.
    /// </summary>
    internal static ModAssemblyLoadContext CreateForPackage(string identity) =>
        new($"ModShardLauncher.Mods/{identity}");

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
