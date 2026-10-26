using ModShardLauncher;

namespace ModShardLauncherTest;

/// <summary>
/// Every launch appends a new timestamped log file, so the logs folder has to be pruned on
/// start-up; otherwise it grows forever.
/// </summary>
public class LogRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"msl-logs-{Guid.NewGuid():N}");

    public LogRetentionTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // A leftover temp folder must not fail the test run.
        }
    }

    [Fact]
    public void Prune_KeepsTheNewestLogFiles_AndLeavesOtherFilesAlone()
    {
        DateTime start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 30; i++)
        {
            WriteLog($"log_20240101_{i:D4}.txt", start.AddMinutes(i));
        }

        string timings = Path.Combine(_root, "timings_20240101_000000.json");
        File.WriteAllText(timings, "{}");

        IReadOnlyList<string> deleted = LogRetention.Prune(_root);

        Assert.Equal(10, deleted.Count);
        Assert.Equal(LogRetention.MaxLogFiles, Directory.GetFiles(_root, "log_*.txt").Length);
        Assert.True(File.Exists(Path.Combine(_root, "log_20240101_0029.txt")), "the newest file must survive");
        Assert.False(File.Exists(Path.Combine(_root, "log_20240101_0000.txt")), "the oldest file must go");
        Assert.True(File.Exists(timings), "timing reports are not log files");
    }

    [Fact]
    public void Prune_CountsCliLogsToo_AndNeverDeletesTheActiveFile()
    {
        DateTime start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string active = WriteLog("log_20240101_0000.txt", start);
        for (int i = 1; i < 20; i++)
        {
            WriteLog($"log_20240101_{i:D4}.txt", start.AddMinutes(i));
        }

        for (int i = 0; i < 6; i++)
        {
            WriteLog($"cli_20240101_000{i}.log", start.AddMinutes(30 + i));
        }

        LogRetention.Prune(_root, keep: active);

        Assert.True(File.Exists(active), "the log this process writes into must survive");
        Assert.Equal(LogRetention.MaxLogFiles, Directory.GetFiles(_root).Length);
    }

    [Fact]
    public void Prune_WhenNothingIsOverTheLimit_DeletesNothing()
    {
        WriteLog("log_20240101_0000.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Empty(LogRetention.Prune(_root));
    }

    private string WriteLog(string name, DateTime timestamp)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, "log");
        File.SetLastWriteTimeUtc(path, timestamp);
        return path;
    }
}
