using ModShardLauncher;

namespace ModShardLauncherTest;

/// <summary>
/// Locks down the <c>settings.ini</c> rewrite used before starting the game in windowed/fullscreen
/// mode. Only the <c>displayMode</c> line may change; every other line of the player's file must be
/// preserved byte for byte (it also holds key bindings, audio and video settings).
/// </summary>
public class GameLauncherTests
{
    [Fact]
    public void IniValue_RoundTrips()
    {
        foreach (GameDisplayMode mode in Enum.GetValues<GameDisplayMode>())
        {
            string value = GameLauncher.ToIniValue(mode);
            Assert.True(GameLauncher.TryParseDisplayMode(value, out GameDisplayMode parsed));
            Assert.Equal(mode, parsed);
        }

        Assert.False(GameLauncher.TryParseDisplayMode("Fullscreen", out _));
        Assert.False(GameLauncher.TryParseDisplayMode("windowed; rm -rf", out _));
        Assert.False(GameLauncher.TryParseDisplayMode("", out _));
        Assert.False(GameLauncher.TryParseDisplayMode(null, out _));
    }

    [Fact]
    public void ExistingLine_IsReplacedInPlace_AndOtherLinesSurvive()
    {
        // The shape of a real file: [video] sits between [control] and [main].
        List<string> source =
        [
            "[control]",
            "0_0=\"87.000000\"",
            "[video]",
            "displayMode=\"fullscreenBorderless\"",
            "resolution=\"1920x1080\"",
            "[main]",
            "language=\"3.000000\"",
        ];

        List<string> updated = GameLauncher.ApplyDisplayMode(source, GameDisplayMode.Windowed);

        Assert.Equal(source.Count, updated.Count);
        Assert.Equal("displayMode=\"windowed\"", updated[3]);
        Assert.Equal("resolution=\"1920x1080\"", updated[4]);
        Assert.Equal("[main]", updated[5]);
        Assert.Equal("language=\"3.000000\"", updated[6]);
        Assert.Equal("[control]", updated[0]);
    }

    [Fact]
    public void MissingKey_IsInsertedUnderTheVideoSection()
    {
        List<string> source =
        [
            "[sound]",
            "sfx gain=\"1.000000\"",
            "[video]",
            "resolution=\"1280x720\"",
        ];

        List<string> updated = GameLauncher.ApplyDisplayMode(source, GameDisplayMode.FullscreenBorderless);

        int section = updated.IndexOf("[video]");
        Assert.Equal(section + 1, updated.IndexOf("displayMode=\"fullscreenBorderless\""));
        Assert.Contains("resolution=\"1280x720\"", updated);
    }

    [Fact]
    public void MissingSection_IsAppendedAfterABlankLine()
    {
        List<string> updated = GameLauncher.ApplyDisplayMode(
            ["[sound]", "sfx gain=\"1.000000\""],
            GameDisplayMode.Windowed);

        int section = updated.IndexOf("[video]");
        Assert.True(section > 0);
        Assert.Equal(string.Empty, updated[section - 1]);
        Assert.Equal("displayMode=\"windowed\"", updated[section + 1]);
    }

    [Fact]
    public void EmptyFile_GetsJustTheSectionAndTheValue()
    {
        List<string> updated = GameLauncher.ApplyDisplayMode([], GameDisplayMode.Windowed);

        Assert.Equal(["[video]", "displayMode=\"windowed\""], updated);
    }

    [Fact]
    public void ExistingLine_IsRecognisedRegardlessOfCaseAndSpacing()
    {
        List<string> updated = GameLauncher.ApplyDisplayMode(
            ["[video]", "  DISPLAYMODE = \"fullscreenBorderless\"  "],
            GameDisplayMode.Windowed);

        Assert.Equal("displayMode=\"windowed\"", updated[1]);
    }

    [Fact]
    public void PreviousModeInTheSameLine_IsNotDuplicated()
    {
        List<string> updated = GameLauncher.ApplyDisplayMode(
            ["[video]", "displayMode=\"windowed\"", "vsync=\"1.000000\""],
            GameDisplayMode.FullscreenBorderless);

        Assert.Single(updated, line => line.Contains("displayMode", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("displayMode=\"fullscreenBorderless\"", updated);
    }

    /// <summary>
    /// The real write: only the display mode changes, the rest of the player's file (key bindings,
    /// audio, video) must come back unchanged, as UTF-8 without a BOM and with CRLF line endings.
    /// </summary>
    [Fact]
    public void WriteDisplayMode_PreservesEverythingElse()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"msl-ini-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.ini");

        try
        {
            const string original =
                "[control]\r\n0_0=\"87.000000\"\r\n[video]\r\n" +
                "displayMode=\"windowed\"\r\nresolution=\"1920x1080\"\r\n[main]\r\nlanguage=\"3.000000\"\r\n";
            File.WriteAllText(path, original, new System.Text.UTF8Encoding(false));

            GameLauncher.WriteDisplayMode(path, GameDisplayMode.FullscreenBorderless);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "the file must stay UTF-8 without a BOM");

            string expected = original.Replace(
                "displayMode=\"windowed\"",
                "displayMode=\"fullscreenBorderless\"",
                StringComparison.Ordinal);
            Assert.Equal(expected, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A missing file must be created with just the section and the value.</summary>
    [Fact]
    public void WriteDisplayMode_CreatesAMissingFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"msl-ini-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.ini");

        try
        {
            GameLauncher.WriteDisplayMode(path, GameDisplayMode.Windowed);
            Assert.Equal("[video]\r\ndisplayMode=\"windowed\"", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>
    /// A direct launch only survives because Steamworks finds the app id in <c>steam_appid.txt</c>;
    /// without it <c>SteamAPI_RestartAppIfNecessary</c> hands the launch back to Steam and the process
    /// exits. The file must be plain ASCII/UTF-8 without a BOM, as the runtime parses it verbatim.
    /// </summary>
    [Fact]
    public void EnsureSteamAppIdFile_WritesTheAppId_WithoutABom()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"msl-appid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            GameLauncher.EnsureSteamAppIdFile(directory);

            string path = Path.Combine(directory, GameLauncher.SteamAppIdFileName);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "the file must be UTF-8 without a BOM");
            Assert.Equal(GameLauncher.SteamAppId, File.ReadAllText(path).Trim());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A file that already holds the right id is not rewritten (the game folder may be read-only).</summary>
    [Fact]
    public void EnsureSteamAppIdFile_LeavesACorrectFileAlone()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"msl-appid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, GameLauncher.SteamAppIdFileName);

        try
        {
            File.WriteAllText(path, GameLauncher.SteamAppId);
            File.SetAttributes(path, FileAttributes.ReadOnly);

            GameLauncher.EnsureSteamAppIdFile(directory);

            File.SetAttributes(path, FileAttributes.Normal);
            Assert.Equal(GameLauncher.SteamAppId, File.ReadAllText(path).Trim());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            Directory.Delete(directory, true);
        }
    }

    /// <summary>A folder only counts as the installation when the executable is really in it.</summary>
    [Fact]
    public void IsGameFolder_RequiresTheExecutable()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"msl-game-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            Assert.False(GameLauncher.IsGameFolder(directory));
            Assert.False(GameLauncher.IsGameFolder(null));
            Assert.False(GameLauncher.IsGameFolder(string.Empty));

            File.WriteAllText(Path.Combine(directory, GameLauncher.GameExecutableName), string.Empty);

            Assert.True(GameLauncher.IsGameFolder(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>VDF escapes the separators, and the extra libraries live outside the Steam root.</summary>
    [Fact]
    public void ReadLibraryFolders_ParsesEscapedPaths()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"msl-vdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "libraryfolders.vdf");

        try
        {
            File.WriteAllText(
                path,
                """
                "libraryfolders"
                {
                    "0"
                    {
                        "path"		"C:\\Program Files (x86)\\Steam"
                        "label"		""
                    }
                    "1"
                    {
                        "path"		"D:\\SteamLibrary"
                        "apps"
                        {
                            "625960"		"160773648"
                        }
                    }
                }
                """);

            string[] libraries = GameLauncher.ReadLibraryFolders(path).ToArray();

            string[] expected = [@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"];
            Assert.Equal(expected, libraries);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>The id of another game would make Steamworks report the wrong app, so it is replaced.</summary>
    [Fact]
    public void EnsureSteamAppIdFile_RepairsAStaleValue()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"msl-appid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, GameLauncher.SteamAppIdFileName);

        try
        {
            File.WriteAllText(path, "480");

            GameLauncher.EnsureSteamAppIdFile(directory);

            Assert.Equal(GameLauncher.SteamAppId, File.ReadAllText(path).Trim());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            Directory.Delete(directory, true);
        }
    }
}
