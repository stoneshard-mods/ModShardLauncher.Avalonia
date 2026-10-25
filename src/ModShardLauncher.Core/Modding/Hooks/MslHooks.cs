namespace ModShardLauncher;

public static partial class Msl
{
    /// <summary>
    /// The script console was removed, so there is no longer a transport for hook payloads.
    /// The method stays for binary compatibility and always fails.
    /// </summary>
    [Obsolete("The script console was removed; HookFunction no longer injects anything.", true)]
    public static void HookFunction(string functionName, string hookName, params string[] paramNames) =>
        throw new NotSupportedException(
            "The script console was removed; Msl.HookFunction is no longer supported.");
}
