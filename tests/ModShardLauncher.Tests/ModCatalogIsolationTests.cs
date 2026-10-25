using ModShardLauncher;

namespace ModShardLauncherTest;

/// <summary>
/// B9: a mod whose constructor throws must be reported and skipped, while the remaining mods
/// still load. Before the fix the exception aborted the whole catalog scan.
/// </summary>
public class ModCatalogIsolationTests
{
    [Fact]
    public void Reload_KeepsGoodModsWhenAnotherConstructorThrows()
    {
        string root = Path.Combine(Path.GetTempPath(), $"msl-b9-{Guid.NewGuid():N}");
        string modsDirectory = Path.Combine(root, "Mods");
        string sourcesDirectory = Path.Combine(root, "ModSources");
        string goodSource = Path.Combine(root, "src", "GoodMod");
        string badSource = Path.Combine(root, "src", "BadMod");
        Directory.CreateDirectory(modsDirectory);
        Directory.CreateDirectory(sourcesDirectory);
        Directory.CreateDirectory(goodSource);
        Directory.CreateDirectory(badSource);

        try
        {
            WriteModSource(goodSource, "GoodMod", constructorBody: string.Empty);
            WriteModSource(badSource, "BadMod", constructorBody: "throw new InvalidOperationException(\"boom\");");

            ModCompiler compiler = new(modsDirectory);
            Assert.True(compiler.Compile(goodSource).Succeeded, "good mod must compile");
            Assert.True(compiler.Compile(badSource).Succeeded, "bad mod must compile");

            ModCatalog catalog = new(modsDirectory, sourcesDirectory);
            catalog.Reload();

            Assert.Single(catalog.Mods);
            Assert.Equal("GoodMod", catalog.Mods[0].Name);
            Assert.Contains(catalog.LoadErrors, error =>
                error.Contains("BadMod", StringComparison.Ordinal) &&
                error.Contains("InvalidOperationException", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void WriteModSource(string directory, string className, string constructorBody)
    {
        File.WriteAllText(Path.Combine(directory, "Mod.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.1</TargetFramework>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        string constructor = constructorBody.Length == 0
            ? string.Empty
            : $"public {className}() {{ {constructorBody} }}";

        File.WriteAllText(Path.Combine(directory, className + ".cs"), $$"""
            using ModShardLauncher.Mods;

            public class {{className}} : Mod
            {
                {{constructor}}
            }
            """);
    }
}
