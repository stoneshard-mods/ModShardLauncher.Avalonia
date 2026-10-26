using ModShardLauncher;
using Serilog;

namespace ModShardLauncher.CLI;

internal static class CliCommands
{
    public static async Task<int> PatchAsync(CliArguments arguments)
    {
        string dataPath = arguments.Require("data");
        string outputPath = arguments.Require("output");

        PatchTimings.Reset();

        GameDataService gameData = new();
        await gameData.LoadAsync(dataPath);

        ModCatalog catalog = new(arguments.Get("mods"));
        catalog.Reload();

        if (arguments.Has("all"))
        {
            foreach (ModFile mod in catalog.Mods)
            {
                mod.Enabled = true;
            }
        }
        else if (arguments.Get("enable") is { } enabled)
        {
            catalog.ApplyEnabledMods(enabled.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        int enabledCount = catalog.Mods.Count(m => m.Enabled);
        if (enabledCount == 0)
        {
            Console.Error.WriteLine("No mod is enabled; pass --all or --enable <names>.");
            return 1;
        }

        PatchRunner runner = new();
        PatchDiagnostic? diagnostic = runner.Patch(catalog.Mods);
        if (diagnostic is not null)
        {
            Console.Error.WriteLine(diagnostic.Title);
            Console.Error.WriteLine(diagnostic.Summary);
            Console.Error.WriteLine(diagnostic.Details);
            return 2;
        }

        await gameData.SaveAsync(outputPath);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (directory is not null)
        {
            LootTableRegistry.SaveLootTables(directory);
        }

        Log.Information("Patched {Count} mod(s) into {Output}", enabledCount, outputPath);
        Console.WriteLine($"Patched {enabledCount} mod(s) into {outputPath}");

        Log.Debug("{Timings}", PatchTimings.ToSummary());
        if (arguments.Get("timings") is { } timingsPath)
        {
            string fullPath = Path.GetFullPath(timingsPath);
            File.WriteAllText(fullPath, PatchTimings.ToJson());
            Console.WriteLine(PatchTimings.ToSummary());
            Console.WriteLine($"Timings written to {fullPath}");
        }

        return 0;
    }

    public static int List(CliArguments arguments)
    {
        ModCatalog catalog = new(arguments.Get("mods"));
        catalog.Reload();

        Console.WriteLine($"{"NAME",-36}{"VERSION",-14}{"ENABLED",-9}");
        Console.WriteLine(new string('-', 59));
        foreach (ModFile mod in catalog.Mods)
        {
            Console.WriteLine($"{mod.Name,-36}{mod.Version,-14}{(mod.Enabled ? "yes" : "no"),-9}");
        }

        foreach (ModSource source in catalog.ModSources)
        {
            Console.WriteLine($"{"[source] " + source.Name,-36}{"-",-14}{"-",-9}");
        }

        foreach (string error in catalog.LoadErrors)
        {
            Console.Error.WriteLine(error);
        }

        return 0;
    }

    public static Task<int> CompileAsync(CliArguments arguments)
    {
        string source = arguments.Require("source");
        ModCompiler compiler = new(arguments.Get("mods"));
        ModCompileResult result = compiler.Compile(source);

        if (result.Succeeded)
        {
            Console.WriteLine($"Compiled {source} into {result.OutputDirectory}");
            return Task.FromResult(0);
        }

        Console.Error.WriteLine($"Compilation of {source} failed. See the log for details.");
        return Task.FromResult(2);
    }

    public static async Task<int> ExportAsync(CliArguments arguments)
    {
        string dataPath = arguments.Require("data");
        string typeName = arguments.Require("type");

        DataType type = typeName.ToLowerInvariant() switch
        {
            "items" => DataType.Items,
            "codes" => DataType.Codes,
            "presets" or "dungeonpresets" => DataType.DungeonPresets,
            _ => throw new ArgumentException($"Unknown export type '{typeName}'."),
        };

        GameDataService gameData = new();
        await gameData.LoadAsync(dataPath);
        gameData.Export(type);

        Console.WriteLine($"Exported {type} from {dataPath} into {Directory.GetCurrentDirectory()}");
        return 0;
    }
}
