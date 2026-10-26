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
/// Starts Stoneshard through Steam, optionally switching its display mode first.
/// <para>
/// Same approach as the stone-tool-box launcher: the game is not started directly (that would bypass
/// Steam's own launch options and cloud sync), it is started with the registered Steam protocol
/// handler, i.e. <c>steam://rungameid/625960</c>. The display mode lives in the game's own
/// <c>%LOCALAPPDATA%\StoneShard\settings.ini</c> under <c>[video]</c>; only the
/// <c>displayMode="…"</c> line is rewritten, every other line is preserved verbatim.
/// </para>
/// </summary>
public static class GameLauncher
{
    /// <summary>Steam application id of Stoneshard.</summary>
    public const string SteamAppId = "625960";

    /// <summary>URI the Steam protocol handler turns into "run app 625960".</summary>
    public const string SteamRunUri = "steam://rungameid/" + SteamAppId;

    private const string ProcessName = "Stoneshard";
    private const string SettingsFileName = "settings.ini";

    /// <summary>Ini line that carries the display mode (case-insensitive, optional whitespace).</summary>
    private static readonly Regex DisplayModeLine = new(
        @"^\s*displaymode\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VideoSectionLine = new(
        @"^\s*\[video\]\s*$",
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
    /// Starts Stoneshard through Steam. When <paramref name="displayMode"/> is given the game's
    /// display mode is written first, so the next start uses it.
    /// </summary>
    /// <exception cref="InvalidOperationException">Steam is not installed/running, or the settings file cannot be written.</exception>
    public static void Launch(GameDisplayMode? displayMode = null)
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
}
