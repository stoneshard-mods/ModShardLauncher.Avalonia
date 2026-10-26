using System.Reflection;
using ModShardLauncher;
using ModShardLauncher.Mods;

namespace ModShardLauncherTest;

/// <summary>
/// Mod assemblies are loaded into a non-collectible load context, so reloading an unchanged
/// <c>.sml</c> used to leak one assembly per patch run. Unchanged packages now keep their assembly
/// while still getting a fresh <see cref="Mod"/> instance.
/// <para>
/// A rebuilt package keeps its assembly identity, and the runtime refuses to load that identity twice
/// into one context ("Assembly with same name is already loaded"). Each package build therefore gets
/// its own context; <c>Reload_PicksUpARecompiledPackage</c> is the regression test for the mod
/// silently disappearing after it was recompiled.
/// </para>
/// </summary>
public class ModCatalogReuseTests
{
    [Fact]
    public void Reload_ReusesTheAssemblyAndThePackage_ButCreatesAFreshModInstance()
    {
        using CatalogWorkspace workspace = CatalogWorkspace.Create("ReuseMod");

        ModCatalog catalog = new(workspace.ModsDirectory, workspace.SourcesDirectory);
        catalog.Reload();

        ModFile first = Assert.Single(catalog.Mods);
        Assembly firstAssembly = first.Assembly!;
        Mod firstInstance = first.instance!;

        catalog.Reload();

        ModFile second = Assert.Single(catalog.Mods);
        Assert.Same(first, second);
        Assert.Same(firstAssembly, second.Assembly);
        Assert.NotSame(firstInstance, second.instance);
        Assert.Equal(first.Enabled, second.Enabled);
    }

    [Fact]
    public void Reload_PicksUpARecompiledPackage()
    {
        using CatalogWorkspace workspace = CatalogWorkspace.Create("RebuiltMod");

        ModCatalog catalog = new(workspace.ModsDirectory, workspace.SourcesDirectory);
        catalog.Reload();

        ModFile first = Assert.Single(catalog.Mods);
        Assert.Null(MarkerOf(first.Assembly!));

        workspace.Recompile(withMarker: true);
        catalog.Reload();

        Assert.Empty(catalog.LoadErrors);
        ModFile second = Assert.Single(catalog.Mods);
        Assert.NotSame(first, second);
        Assert.NotSame(first.Assembly, second.Assembly);
        Assert.NotNull(MarkerOf(second.Assembly!));
    }

    [Fact]
    public void Reload_DropsTheCacheWhenThePackageDisappears()
    {
        using CatalogWorkspace workspace = CatalogWorkspace.Create("RemovedMod");

        ModCatalog catalog = new(workspace.ModsDirectory, workspace.SourcesDirectory);
        catalog.Reload();
        ModFile first = Assert.Single(catalog.Mods);

        File.Delete(first.Path);
        catalog.Reload();

        Assert.Empty(catalog.Mods);
    }

    private static MethodInfo? MarkerOf(Assembly assembly) =>
        assembly.GetTypes().SelectMany(t => t.GetMethods()).FirstOrDefault(m => m.Name == "Marker");

    /// <summary>Compiles one real mod package into a temporary mods folder.</summary>
    private sealed class CatalogWorkspace : IDisposable
    {
        private readonly string _className;

        private CatalogWorkspace(string root, string modsDirectory, string sourcesDirectory, string source, string className)
        {
            Root = root;
            ModsDirectory = modsDirectory;
            SourcesDirectory = sourcesDirectory;
            Source = source;
            _className = className;
        }

        public string Root { get; }

        public string ModsDirectory { get; }

        public string SourcesDirectory { get; }

        public string Source { get; }

        public static CatalogWorkspace Create(string className)
        {
            string root = Path.Combine(Path.GetTempPath(), $"msl-reuse-{Guid.NewGuid():N}");
            string modsDirectory = Path.Combine(root, "Mods");
            string sourcesDirectory = Path.Combine(root, "ModSources");
            string source = Path.Combine(root, "src", className);
            Directory.CreateDirectory(modsDirectory);
            Directory.CreateDirectory(sourcesDirectory);
            Directory.CreateDirectory(source);

            CatalogWorkspace workspace = new(root, modsDirectory, sourcesDirectory, source, className);
            workspace.Recompile(withMarker: false);
            return workspace;
        }

        /// <summary>Rebuilds the package; a recompile keeps the assembly identity but changes the bytes.</summary>
        public void Recompile(bool withMarker)
        {
            File.WriteAllText(Path.Combine(Source, "Mod.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.1</TargetFramework>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                </Project>
                """);

            string marker = withMarker ? "public static void Marker() { }" : string.Empty;
            File.WriteAllText(Path.Combine(Source, _className + ".cs"), $$"""
                using ModShardLauncher.Mods;

                public class {{_className}} : Mod
                {
                    {{marker}}
                }
                """);

            ModCompiler compiler = new(ModsDirectory);
            if (!compiler.Compile(Source).Succeeded)
            {
                throw new InvalidOperationException($"cannot compile the test mod {_className}");
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch
            {
                // Best effort: a locked temp folder must not fail the test.
            }
        }
    }
}
