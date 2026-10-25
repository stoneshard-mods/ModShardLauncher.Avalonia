using Serilog;
using UndertaleModLib;

namespace ModShardLauncher;

/// <summary>
/// Binary compatibility facade for mods compiled against the WPF build.
/// <para>
/// The old launcher exposed the current game data as a public static field
/// (<c>DataLoader.data</c>). Compiled mods use <c>ldsfld</c> on that exact field, so it cannot be
/// replaced by a property. <see cref="MslContext"/> forwards to this field to keep a single source
/// of truth.
/// </para>
/// </summary>
public class DataLoader
{
    /// <summary>The data.win currently loaded by the host (legacy API).</summary>
    public static UndertaleData data = new();

    /// <summary>Legacy entry point; the host owns file loading in the Avalonia build.</summary>
    public static Task<bool> DoOpenDialog() =>
        throw new NotSupportedException("Open dialogs are owned by the desktop host; use the launcher UI.");

    /// <summary>Legacy entry point; the host owns file loading in the Avalonia build.</summary>
    public static Task LoadFile(string filename) =>
        throw new NotSupportedException("Use GameDataService.LoadAsync from the host; mods should not load data files.");

    /// <summary>Legacy entry point; the host owns file saving in the Avalonia build.</summary>
    public static Task<bool> DoSaveDialog() =>
        throw new NotSupportedException("Save dialogs are owned by the desktop host.");

    /// <summary>Legacy entry point; the host owns file saving in the Avalonia build.</summary>
    public static Task SaveFile(string filename) =>
        throw new NotSupportedException("Use GameDataService.SaveAsync from the host; mods should not save data files.");

    public static void Reset() => MslContext.Detach();

    public static void LogUTMTWarnings(string warning) => Log.Warning("[UTMT WARNING]: {0}", warning);

    public static void LogUTMTMessages(string message)
    {
        // UndertaleModLib chatty messages stay silent, as in the WPF build.
    }
}
