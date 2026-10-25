using ModShardLauncher;
using Serilog;

namespace ModShardLauncher.CLI;

/// <summary>Headless entry point used when the executable is started with command line arguments.</summary>
internal static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        CliArguments arguments;
        try
        {
            arguments = CliArguments.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            CliArguments.PrintUsage();
            return 1;
        }

        if (arguments.ShowHelp)
        {
            CliArguments.PrintUsage();
            return 0;
        }

        if (arguments.ShowVersion)
        {
            Console.WriteLine($"{AppInfo.ProductName} {AppInfo.MslVersion} (UndertaleModTool {AppInfo.UtmtLibVersion})");
            return 0;
        }

        AppPaths.DataDirectory = arguments.DataDirectory;

        // An explicit --data-dir means the caller owns that folder; never pull data into it.
        // Migrate before the logger creates the new logs folder.
        IReadOnlyList<string> migrated = arguments.Has("data-dir") || arguments.Has("workdir")
            ? Array.Empty<string>()
            : AppPaths.MigratePortableLayout();

        AppPaths.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(Path.Combine(AppPaths.LogsDirectory, $"cli_{DateTime.Now:yyyyMMdd_HHmmss}.log"))
            .CreateLogger();

        Log.Information("User data folder: {Directory}", AppPaths.DataDirectory);
        foreach (string item in migrated)
        {
            Log.Information("Migrated {Item} from the portable layout", item);
        }

        try
        {
            return arguments.Command switch
            {
                "patch" => await CliCommands.PatchAsync(arguments),
                "list" => CliCommands.List(arguments),
                "compile" => await CliCommands.CompileAsync(arguments),
                "export" => await CliCommands.ExportAsync(arguments),
                _ => Unknown(arguments.Command),
            };
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled failure");
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        CliArguments.PrintUsage();
        return 1;
    }
}
