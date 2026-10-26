using System.Diagnostics;
using System.Reflection;
using UndertaleModLib.Models;

namespace ModShardLauncher;

/// <summary>Version information shown in the UI, logs and baked into compiled mods.</summary>
public static class AppInfo
{
    public const string ProductName = "ModShardLauncher";

    /// <summary>Address of this Avalonia refactor.</summary>
    public const string RepositoryUrl = "https://github.com/stoneshard-mods/ModShardLauncher.Avalonia";

    /// <summary>The original WPF launcher this Avalonia build was refactored from.</summary>
    public const string SourceRepositoryUrl = "https://github.com/ModShardTeam/ModShardLauncher";

    private static readonly Lazy<string> MslLazy = new(() => "v" + ReadVersion(typeof(AppInfo).Assembly, "0.13.2.0"));
    private static readonly Lazy<string> UtmtLazy = new(() => "v" + ReadVersion(typeof(UndertaleCode).Assembly, "0.6.1.0"));

    /// <summary>Launcher version, matching the value stored in <c>Mod.Version</c> when packed.</summary>
    public static string MslVersion => MslLazy.Value;

    /// <summary>Version of the frozen UndertaleModTool libraries.</summary>
    public static string UtmtLibVersion => UtmtLazy.Value;

    private static string ReadVersion(Assembly assembly, string fallback)
    {
        try
        {
            string location = assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                return FileVersionInfo.GetVersionInfo(location).FileVersion ?? fallback;
            }
        }
        catch
        {
            // A single-file publish has no Location; fall back to the compiled-in version.
        }

        return fallback;
    }
}
