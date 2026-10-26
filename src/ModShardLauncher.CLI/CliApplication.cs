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

        string logFile = Path.Combine(AppPaths.LogsDirectory, $"cli_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logFile)
            .CreateLogger();

        // Older runs leave one file each; keep the folder capped at the newest ones.
        LogRetention.Prune(AppPaths.LogsDirectory, keep: logFile);

        Log.Information("User data folder: {Directory}", AppPaths.DataDirectory);
        foreach (string item in migrated)
        {
            Log.Information("Migrated {Item} from the portable layout", item);
        }

        // Ctrl+C asks the pipeline to stop instead of killing it: the pipeline only breaks between
        // mods, and the save stage discards its temp file, so no partial output is left behind.
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler onCancel = (_, args) =>
        {
            args.Cancel = true;
            Log.Warning("Cancellation requested; stopping at the next safe point");
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            return arguments.Command switch
            {
                "patch" => await CliCommands.PatchAsync(arguments, cancellation.Token),
                "list" => CliCommands.List(arguments),
                "launch" => CliCommands.Launch(arguments),
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
            Console.CancelKeyPress -= onCancel;
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
