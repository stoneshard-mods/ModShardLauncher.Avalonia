using System.Runtime.CompilerServices;

namespace ModShardLauncherTest;

/// <summary>
/// Process-wide test setup. The optional decompiler cache is written next to the user data by
/// default; tests must never touch the developer's real <c>%LOCALAPPDATA%\ModShardLauncher</c>,
/// so they get their own folder (stable across runs, so the cache-hit path is exercised too).
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        string cacheDirectory = Path.Combine(Path.GetTempPath(), "msl-test-decompiler-cache");
        Environment.SetEnvironmentVariable("MSL_CACHE_DIR", cacheDirectory);
    }
}
