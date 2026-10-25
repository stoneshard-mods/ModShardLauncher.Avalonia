using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ModShardLauncher.Mods;

namespace ModShardLauncherTest;

/// <summary>
/// Reproduces the old-package loading path: a mod assembly is compiled against the launcher,
/// loaded from bytes and reflected over. Dependencies (notably ModShardLauncher itself) must
/// resolve through <see cref="ModAssemblyLoadContext"/>.
/// </summary>
public class ModAssemblyLoadingTests
{
    private const string ModSource = """
        using ModShardLauncher.Mods;

        namespace TestMod;

        public sealed class TestModEntry : Mod
        {
            public override string Name => "TestMod";

            public override void PatchMod()
            {
            }
        }
        """;

    [Fact]
    public void LoadedModAssembly_ResolvesLauncherContractAndModBaseType()
    {
        byte[] assemblyBytes = CompileMod();

        var assembly = ModAssemblyLoadContext.Instance.LoadMod(assemblyBytes);
        Type[] types = assembly.GetTypes();

        Type modType = Assert.Single(types.Where(t => t.IsSubclassOf(typeof(Mod))));
        Assert.Equal("TestModEntry", modType.Name);
        Assert.Equal(typeof(Mod), modType.BaseType);
        Assert.Equal("TestMod", Activator.CreateInstance(modType)!.ToString());
    }

    private static byte[] CompileMod()
    {
        List<MetadataReference> references = new();
        string trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        references.Add(MetadataReference.CreateFromFile(typeof(Mod).Assembly.Location));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestModAssembly",
            [CSharpSyntaxTree.ParseText(ModSource)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }
}
