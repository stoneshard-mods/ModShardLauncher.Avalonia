namespace ModShardLauncher;

/// <summary>
/// Keeps <see cref="AppPaths.LogsDirectory"/> from growing without bound.
/// <para>
/// Every launch writes a fresh timestamped file (<c>log_*.txt</c> in the GUI,
/// <c>cli_*.log</c> in the CLI), so Serilog's own <c>retainedFileCountLimit</c> - which only
/// applies to rolling files - cannot cap the folder. The old files are pruned explicitly.
/// </para>
/// </summary>
public static class LogRetention
{
    /// <summary>Number of log files kept in the logs folder, the newest ones win.</summary>
    public const int MaxLogFiles = 20;

    private static readonly string[] Prefixes = ["log_", "cli_"];

    private static readonly string[] Extensions = [".txt", ".log"];

    /// <summary>
    /// Deletes the oldest log files until at most <paramref name="maxFiles"/> remain.
    /// </summary>
    /// <param name="directory">Folder to prune; defaults to <see cref="AppPaths.LogsDirectory"/>.</param>
    /// <param name="maxFiles">Maximum number of log files to keep.</param>
    /// <param name="keep">
    /// File that must survive the pruning (the log this process is writing into). It still counts
    /// against <paramref name="maxFiles"/>.
    /// </param>
    /// <returns>The paths that were deleted; empty when nothing had to go.</returns>
    public static IReadOnlyList<string> Prune(string? directory = null, int maxFiles = MaxLogFiles, string? keep = null)
    {
        if (maxFiles <= 0)
        {
            return Array.Empty<string>();
        }

        string folder = directory ?? AppPaths.LogsDirectory;
        if (!Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        string? active = string.IsNullOrEmpty(keep) ? null : Path.GetFullPath(keep);
        List<FileInfo> logs;
        try
        {
            logs = new DirectoryInfo(folder)
                .EnumerateFiles()
                .Where(file => IsLogFile(file.Name))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        // The active file is never deleted but occupies one of the slots.
        int budget = maxFiles - logs.Count(file => IsActive(file, active));
        List<string> deleted = new();
        foreach (FileInfo log in logs)
        {
            if (IsActive(log, active))
            {
                continue;
            }

            if (budget > 0)
            {
                budget--;
                continue;
            }

            try
            {
                log.Delete();
                deleted.Add(log.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another instance may still have the file open; the next launch retries.
            }
        }

        return deleted;
    }

    private static bool IsLogFile(string name) =>
        Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
        Extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static bool IsActive(FileInfo file, string? active) =>
        active is not null && string.Equals(file.FullName, active, StringComparison.OrdinalIgnoreCase);
}
