namespace ModShardLauncher.CLI;

/// <summary>Small, dependency free command line parser for the headless launcher.</summary>
internal sealed class CliArguments
{
    private CliArguments()
    {
    }

    public string Command { get; private init; } = string.Empty;

    public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string DataDirectory { get; private init; } = AppPaths.GetDefaultDataDirectory();

    public bool ShowHelp => Flags.Contains("help") || Flags.Contains("h");

    public bool ShowVersion => Flags.Contains("version") || Flags.Contains("v");

    public string? Get(string name) => Options.TryGetValue(name, out string? value) ? value : null;

    public bool Has(string name) => Flags.Contains(name) || Options.ContainsKey(name);

    public string Require(string name) =>
        Get(name) ?? throw new ArgumentException($"Missing required option --{name}");

    public static CliArguments Parse(string[] args)
    {
        CliArguments parsed = new();
        List<string> positional = new();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "-h" or "--help")
            {
                parsed.Flags.Add("help");
                continue;
            }

            if (arg is "-v" or "--version")
            {
                parsed.Flags.Add("version");
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                string name = arg[2..];
                if (name.Length == 0)
                {
                    throw new ArgumentException("Invalid option '--'.");
                }

                int equals = name.IndexOf('=');
                if (equals > 0)
                {
                    parsed.Options[name[..equals]] = name[(equals + 1)..];
                }
                else if (name is "all" or "verbose")
                {
                    parsed.Flags.Add(name);
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    parsed.Options[name] = args[++i];
                }
                else
                {
                    parsed.Flags.Add(name);
                }

                continue;
            }

            positional.Add(arg);
        }

        if (positional.Count == 0)
        {
            return parsed;
        }

        string command = positional[0];
        return new CliArguments
        {
            Command = command,
            // --workdir is the legacy name of --data-dir and stays supported.
            DataDirectory = parsed.Get("data-dir")
                ?? parsed.Get("workdir")
                ?? AppPaths.GetDefaultDataDirectory(),
        }.With(parsed);
    }

    private CliArguments With(CliArguments other)
    {
        foreach ((string key, string value) in other.Options)
        {
            Options[key] = value;
        }

        foreach (string flag in other.Flags)
        {
            Flags.Add(flag);
        }

        return this;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            ModShardLauncher CLI - headless mod patching for Stoneshard.

            Usage:
              ModShardLauncher.CLI.exe patch   --data <vanilla.win> --output <patched.win> [options]
              ModShardLauncher.CLI.exe list    [options]
              ModShardLauncher.CLI.exe launch  [--windowed | --fullscreen]
              ModShardLauncher.CLI.exe compile --source <project-dir> [options]
              ModShardLauncher.CLI.exe export  --data <vanilla.win> --type items|codes|presets

            Patch options:
              --mods <dir>          Mods directory (default: <data-dir>/Mods)
              --enable name1,name2  Enable only these mods
              --all                 Enable every installed mod
              --timings <file>      Write the load/patch/save stage breakdown as JSON

            Launch options (starts Stoneshard through Steam):
              --windowed            Set displayMode="windowed" before starting
              --fullscreen          Set displayMode="fullscreenBorderless" before starting

            Global options:
              --data-dir <dir>  Override the user data folder
                                (default: %LOCALAPPDATA%\ModShardLauncher)
              --workdir <dir>   Alias of --data-dir, kept for compatibility
              -h, --help        Show help
              -v, --version     Show version
            """);
    }
}
