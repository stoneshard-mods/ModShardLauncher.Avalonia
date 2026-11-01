using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;

namespace ModShardLauncher;

/// <summary>The two display modes Stoneshard accepts in <c>settings.ini</c>.</summary>
public enum GameDisplayMode
{
    /// <summary><c>displayMode="windowed"</c>.</summary>
    Windowed,

    /// <summary><c>displayMode="fullscreenBorderless"</c>.</summary>
    FullscreenBorderless,
}

/// <summary>
/// Starts Stoneshard, optionally switching its display mode first.
/// <para>
/// By default the game is handed to Steam with the registered protocol handler
/// (<c>steam://rungameid/625960</c>), which keeps Steam's own launch options and cloud sync working.
/// The "direct launch" mode starts <c>StoneShard.exe</c> itself instead, after dropping
/// <see cref="SteamAppIdFileName"/> so the Steamworks runtime no longer restarts the game through
/// Steam; the game runs with the client closed, but its Steam features stay inert.
/// </para>
/// <para>
/// The display mode lives in the game's own <c>%LOCALAPPDATA%\StoneShard\settings.ini</c> under
/// <c>[video]</c>; only the <c>displayMode="…"</c> line is rewritten, every other line is preserved
/// verbatim.
/// </para>
/// </summary>
public static class GameLauncher
{
    /// <summary>Steam application id of Stoneshard.</summary>
    public const string SteamAppId = "625960";

    /// <summary>URI the Steam protocol handler turns into "run app 625960".</summary>
    public const string SteamRunUri = "steam://rungameid/" + SteamAppId;

    /// <summary>
    /// File the Steamworks runtime reads to resolve the app id on its own. Without it
    /// <c>SteamAPI_RestartAppIfNecessary</c> hands the launch back to Steam and the process exits,
    /// so it is the one thing a direct launch has to provide.
    /// </summary>
    public const string SteamAppIdFileName = "steam_appid.txt";

    /// <summary>Executable started by a direct launch (note the capital S).</summary>
    public const string GameExecutableName = "StoneShard.exe";

    /// <summary>Folder name of the game inside a Steam library (<c>steamapps\common</c>).</summary>
    public const string GameFolderName = "Stoneshard";

    /// <summary>Steam roots tried when the installation has to be located on disk.</summary>
    private static readonly string[] SteamRootCandidates =
    [
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam",
        @"C:\Steam",
    ];
    private const string ProcessName = "Stoneshard";
    private const string SettingsFileName = "settings.ini";

    /// <summary>Ini line that carries the display mode (case-insensitive, optional whitespace).</summary>
    private static readonly Regex DisplayModeLine = new(
        @"^\s*displaymode\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VideoSectionLine = new(
        @"^\s*\[video\]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A <c>"path" "…"</c> entry of <c>steamapps\libraryfolders.vdf</c>.</summary>
    private static readonly Regex LibraryPathLine = new(
        @"""path""\s+""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The game's per-user data folder (<c>settings.ini</c>, saves, ...).</summary>
    public static string StoneShardDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StoneShard");

    /// <summary>The game's settings file that carries the display mode.</summary>
    public static string SettingsIniPath => Path.Combine(StoneShardDataDirectory, SettingsFileName);

    /// <summary>The value written into <c>displayMode="…"</c>.</summary>
    public static string ToIniValue(GameDisplayMode mode) => mode switch
    {
        GameDisplayMode.Windowed => "windowed",
        GameDisplayMode.FullscreenBorderless => "fullscreenBorderless",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported display mode."),
    };

    /// <summary>Parses a value from the ini file; unknown values are rejected.</summary>
    public static bool TryParseDisplayMode(string? iniValue, out GameDisplayMode mode)
    {
        if (string.Equals(iniValue?.Trim(), "windowed", StringComparison.OrdinalIgnoreCase))
        {
            mode = GameDisplayMode.Windowed;
            return true;
        }

        if (string.Equals(iniValue?.Trim(), "fullscreenBorderless", StringComparison.OrdinalIgnoreCase))
        {
            mode = GameDisplayMode.FullscreenBorderless;
            return true;
        }

        mode = default;
        return false;
    }

    /// <summary>
    /// Returns <paramref name="lines"/> with <c>displayMode</c> set to <paramref name="mode"/>.
    /// An existing line is replaced in place, otherwise the value is inserted under an existing
    /// <c>[video]</c> section, otherwise a new section is appended.
    /// </summary>
    public static List<string> ApplyDisplayMode(IReadOnlyList<string> lines, GameDisplayMode mode)
    {
        ArgumentNullException.ThrowIfNull(lines);

        string value = $"displayMode=\"{ToIniValue(mode)}\"";
        List<string> updated = new(lines);

        int existing = updated.FindIndex(line => DisplayModeLine.IsMatch(line));
        if (existing >= 0)
        {
            updated[existing] = value;
            return updated;
        }

        int section = updated.FindIndex(line => VideoSectionLine.IsMatch(line));
        if (section >= 0)
        {
            updated.Insert(section + 1, value);
            return updated;
        }

        // No [video] section yet: separate it from the previous one with a blank line.
        if (updated.Count > 0 && updated[^1].Trim().Length > 0)
        {
            updated.Add(string.Empty);
        }

        updated.Add("[video]");
        updated.Add(value);
        return updated;
    }

    /// <summary>The display mode currently stored in the game's settings, if it can be read.</summary>
    public static GameDisplayMode? ReadDisplayMode()
    {
        try
        {
            if (!File.Exists(SettingsIniPath))
            {
                return null;
            }

            foreach (string line in File.ReadAllLines(SettingsIniPath))
            {
                if (!DisplayModeLine.IsMatch(line))
                {
                    continue;
                }

                int equals = line.IndexOf('=');
                string value = equals >= 0 ? line[(equals + 1)..].Trim().Trim('"') : line.Trim();
                return TryParseDisplayMode(value, out GameDisplayMode mode) ? mode : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(ex, "Cannot read the game display mode from {Path}", SettingsIniPath);
        }

        return null;
    }

    /// <summary>
    /// Rewrites only the <c>displayMode</c> line of the game's <c>settings.ini</c>. The file is
    /// written as UTF-8 without a BOM and with CRLF line endings, which is how the game writes it.
    /// </summary>
    public static void SetDisplayMode(GameDisplayMode mode) => WriteDisplayMode(SettingsIniPath, mode);

    /// <summary>The file operation behind <see cref="SetDisplayMode"/>; the path is injectable for tests.</summary>
    internal static void WriteDisplayMode(string iniPath, GameDisplayMode mode)
    {
        string? directory = Path.GetDirectoryName(iniPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string original = File.Exists(iniPath) ? File.ReadAllText(iniPath) : string.Empty;

        // Keep the file's own shape: same line order, CRLF endings and (if present) a trailing
        // newline, so the game's file only differs by the one line we change.
        string normalized = original.Replace("\r\n", "\n").Replace('\r', '\n');
        List<string> lines = normalized.Length == 0
            ? new List<string>()
            : new List<string>(normalized.Split('\n'));
        bool trailingNewline = lines.Count > 0 && lines[^1].Length == 0;
        if (trailingNewline)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        List<string> updated = ApplyDisplayMode(lines, mode);
        string content = string.Join("\r\n", updated);
        if (trailingNewline)
        {
            content += "\r\n";
        }

        try
        {
            File.WriteAllText(iniPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Cannot write the game settings file '{iniPath}'. Close Stoneshard and try again.",
                ex);
        }

        Log.Information("Display mode set to {Mode} in {Path}", ToIniValue(mode), iniPath);
    }

    /// <summary>Whether Stoneshard is currently running (used to warn before changing its settings).</summary>
    public static bool IsGameRunning()
    {
        try
        {
            return Process.GetProcessesByName(ProcessName).Length > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            Log.Debug(ex, "Cannot enumerate the Stoneshard process");
            return false;
        }
    }

    /// <summary>
    /// Starts Stoneshard, either through Steam (the default) or straight from its own executable.
    /// When <paramref name="displayMode"/> is given the game's display mode is written first, so the
    /// next start uses it.
    /// </summary>
    /// <param name="displayMode">Display mode to persist before starting, or <c>null</c> to keep the current one.</param>
    /// <param name="directLaunch">
    /// When <c>true</c> the game executable is started directly instead of being handed to Steam, which
    /// costs achievements, rich presence and cloud sync while the Steam client stays closed.
    /// </param>
    /// <param name="gameDirectory">Folder holding <see cref="GameExecutableName"/>; required for a direct launch.</param>
    /// <exception cref="InvalidOperationException">
    /// The launch cannot be started, the game folder is not writable, or the settings file cannot be written.
    /// </exception>
    public static void Launch(
        GameDisplayMode? displayMode = null,
        bool directLaunch = false,
        string? gameDirectory = null)
    {
        if (displayMode is { } mode)
        {
            SetDisplayMode(mode);
        }

        if (IsGameRunning())
        {
            Log.Warning(
                "Stoneshard is already running; a display-mode change only takes effect on the next start");
        }

        if (directLaunch)
        {
            LaunchDirect(gameDirectory);
            return;
        }

        Log.Information(
            "Starting Stoneshard through Steam ({Uri}); display mode is {Mode}",
            SteamRunUri,
            ReadDisplayMode()?.ToString() ?? "unknown");

        try
        {
            Process.Start(new ProcessStartInfo(SteamRunUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException(
                "Cannot start Stoneshard through Steam. Make sure Steam is installed and running, then try again.",
                ex);
        }
    }

    /// <summary>
    /// Starts <see cref="GameExecutableName"/> from <paramref name="gameDirectory"/> while Steam stays
    /// closed. Without the Steam client the game runs, but Steam features are inert.
    /// </summary>
    private static void LaunchDirect(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new InvalidOperationException(
                "The Stoneshard installation folder is unknown. Set it in Settings under \"Direct launch\", or open the game's data.win once so the launcher can remember it.");
        }

        string executable = Path.Combine(gameDirectory, GameExecutableName);
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException(
                $"Cannot find '{executable}'. Point the game folder in Settings at the folder that contains {GameExecutableName}.");
        }

        EnsureSteamAppIdFile(gameDirectory);

        Log.Information(
            "Starting Stoneshard directly from {Path} (Steam bypassed); display mode is {Mode}",
            executable,
            ReadDisplayMode()?.ToString() ?? "unknown");

        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                WorkingDirectory = gameDirectory,
                UseShellExecute = false,
            })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException($"Cannot start '{executable}'.", ex);
        }
    }

    /// <summary>Whether <paramref name="directory"/> is the game's installation folder.</summary>
    public static bool IsGameFolder(string? directory) =>
        !string.IsNullOrWhiteSpace(directory)
        && File.Exists(Path.Combine(directory, GameExecutableName));

    /// <summary>
    /// Locates the installation so a direct launch works without the user having opened a data file
    /// first. Walks the usual Steam roots, then the extra libraries listed in
    /// <c>steamapps\libraryfolders.vdf</c>. Returns <c>null</c> when nothing is found.
    /// </summary>
    public static string? TryDetectGameDirectory()
    {
        foreach (string root in SteamRootCandidates)
        {
            string? found = FindInSteamLibrary(root);
            if (found is not null)
            {
                Log.Information("Detected the Stoneshard installation at {Path}", found);
                return found;
            }
        }

        Log.Debug("No Stoneshard installation found in the known Steam roots");
        return null;
    }

    /// <summary>Looks for the game in <paramref name="steamRoot"/> and in the libraries it lists.</summary>
    private static string? FindInSteamLibrary(string steamRoot)
    {
        string direct = Path.Combine(steamRoot, "steamapps", "common", GameFolderName);
        if (IsGameFolder(direct))
        {
            return direct;
        }

        string libraryFolders = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFolders))
        {
            return null;
        }

        try
        {
            foreach (string library in ReadLibraryFolders(libraryFolders))
            {
                string candidate = Path.Combine(library, "steamapps", "common", GameFolderName);
                if (IsGameFolder(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(ex, "Cannot read the Steam library list {Path}", libraryFolders);
        }

        return null;
    }

    /// <summary>Extracts the <c>"path" "…"</c> entries of a <c>libraryfolders.vdf</c> file.</summary>
    internal static IEnumerable<string> ReadLibraryFolders(string vdfPath)
    {
        foreach (string line in File.ReadLines(vdfPath))
        {
            // Fully qualified: this namespace also has a type called Match.
            System.Text.RegularExpressions.Match match = LibraryPathLine.Match(line);
            if (match.Success)
            {
                // VDF escapes backslashes, so "D:\\SteamLibrary" has to become "D:\SteamLibrary".
                yield return match.Groups[1].Value.Replace(@"\\", @"\");
            }
        }
    }

    /// <summary>
    /// Writes <see cref="SteamAppIdFileName"/> (content <see cref="SteamAppId"/>) next to the game
    /// executable when it is missing or stale. The Steamworks runtime then resolves the app id itself,
    /// so <c>SteamAPI_RestartAppIfNecessary</c> no longer restarts the game through Steam and the
    /// process survives outside of the client.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The game folder is not writable, which is the normal case for a non-elevated process under
    /// <c>Program Files</c>.
    /// </exception>
    internal static void EnsureSteamAppIdFile(string gameDirectory)
    {
        string path = Path.Combine(gameDirectory, SteamAppIdFileName);
        try
        {
            if (File.Exists(path)
                && string.Equals(File.ReadAllText(path).Trim(), SteamAppId, StringComparison.Ordinal))
            {
                return;
            }

            File.WriteAllText(path, SteamAppId, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Log.Information("Wrote {Path} so Steamworks resolves app {AppId} without Steam", path, SteamAppId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Cannot write '{path}'. Starting Stoneshard without Steam needs write access to the game folder; "
                + "run the launcher as administrator or use the Steam launch instead.",
                ex);
        }
    }
}
