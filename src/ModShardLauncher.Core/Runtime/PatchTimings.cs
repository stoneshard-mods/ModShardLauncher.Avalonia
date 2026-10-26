using System.Diagnostics;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace ModShardLauncher;

/// <summary>
/// Wall-clock timings for one load → patch → save run.
/// <para>
/// The patch pipeline is dominated by a handful of one-off costs (the first cold decompilation of a
/// big script, the decompiler's authoring caches, saving the 160 MB data file), so any performance
/// work needs a repeatable breakdown instead of log archaeology. Stages are free-form strings:
/// <c>load</c>, <c>save</c>, <c>pipeline</c>, <c>mod:&lt;name&gt;</c>, <c>decompile:&lt;script&gt;</c>,
/// <c>compile:&lt;script&gt;</c>, <c>table:&lt;table&gt;</c>, <c>textures:&lt;mod&gt;</c>.
/// </para>
/// <para>
/// Collection is always on (a stopwatch read plus a list append per stage) so a normal CLI or GUI
/// run can be profiled after the fact; <see cref="Reset"/> delimits one run.
/// </para>
/// </summary>
public static class PatchTimings
{
    private const int JsonSchema = 1;

    private static readonly object Gate = new();
    private static readonly List<(string Stage, double Ms)> Events = new();
    private static DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;

    /// <summary>Starts a new measurement window (everything recorded before it is dropped).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Events.Clear();
            _startedUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Records one stage that was measured by hand.</summary>
    public static void Record(string stage, double milliseconds)
    {
        lock (Gate)
        {
            Events.Add((stage, milliseconds));
        }
    }

    /// <summary>Times the returned scope; dispose it to record the stage.</summary>
    /// <example><c>using (PatchTimings.Measure("load")) { ... }</c></example>
    public static Scope Measure(string stage) => new(stage);

    /// <summary>The raw (stage, milliseconds) sequence of the current window.</summary>
    public static IReadOnlyList<(string Stage, double Ms)> Snapshot()
    {
        lock (Gate)
        {
            return Events.ToArray();
        }
    }

    public static bool IsEmpty
    {
        get
        {
            lock (Gate)
            {
                return Events.Count == 0;
            }
        }
    }

    /// <summary>One aggregated stage: how often it ran, how long it took in total and at most.</summary>
    public sealed record StageTiming(string Stage, int Count, double TotalMs, double MaxMs, double AverageMs);

    /// <summary>Stages grouped by name, slowest total first (the order a reader wants).</summary>
    public static IReadOnlyList<StageTiming> Aggregate()
    {
        return Snapshot()
            .GroupBy(x => x.Stage, StringComparer.Ordinal)
            .Select(g =>
            {
                double total = g.Sum(x => x.Ms);
                double max = g.Max(x => x.Ms);
                return new StageTiming(g.Key, g.Count(), total, max, total / g.Count());
            })
            .OrderByDescending(x => x.TotalMs)
            .ThenBy(x => x.Stage, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Machine readable breakdown, for comparing runs and taking medians.</summary>
    public static string ToJson()
    {
        IReadOnlyList<StageTiming> stages = Aggregate();
        double total = stages.Count == 0 ? 0 : stages
            .Where(x => x.Stage is "load" or "pipeline" or "save")
            .Sum(x => x.TotalMs);

        return JsonConvert.SerializeObject(
            new
            {
                schema = JsonSchema,
                mslVersion = AppInfo.MslVersion,
                utmtLibVersion = AppInfo.UtmtLibVersion,
                startedUtc = _startedUtc.ToString("O", CultureInfo.InvariantCulture),
                totalMs = Math.Round(total, 1),
                stages = stages.Select(x => new
                {
                    stage = x.Stage,
                    count = x.Count,
                    totalMs = Math.Round(x.TotalMs, 1),
                    maxMs = Math.Round(x.MaxMs, 1),
                    averageMs = Math.Round(x.AverageMs, 1),
                }),
                sequence = Snapshot().Select(x => new { stage = x.Stage, ms = Math.Round(x.Ms, 1) }),
            },
            Formatting.Indented);
    }

    /// <summary>One log line with the three top level stages, for the log panel.</summary>
    public static string ToCompactSummary()
    {
        IReadOnlyList<StageTiming> stages = Aggregate();

        static string Stage(IReadOnlyList<StageTiming> all, string name)
        {
            double total = all.Where(x => x.Stage == name).Sum(x => x.TotalMs);
            return total <= 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{total / 1000.0:F1}s");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Timings: load {Stage(stages, "load")}, patch {Stage(stages, "pipeline")}, save {Stage(stages, "save")}");
    }

    /// <summary>
    /// Human readable breakdown for the log: the slowest <paramref name="top"/> stages plus a single
    /// line for everything else. Keeps the log panel readable on a 40-stage run.
    /// </summary>
    public static string ToSummary(int top = 12)
    {
        IReadOnlyList<StageTiming> stages = Aggregate();
        if (stages.Count == 0)
        {
            return "Patch timings: nothing recorded.";
        }

        double covered = stages
            .Where(x => x.Stage is "load" or "pipeline" or "save")
            .Sum(x => x.TotalMs);

        StringBuilder sb = new();
        sb.Append(CultureInfo.InvariantCulture,
            $"Patch timings (load + patch + save = {covered / 1000.0:F1} s)");
        sb.AppendLine();

        int shown = Math.Min(top, stages.Count);
        for (int i = 0; i < shown; i++)
        {
            StageTiming stage = stages[i];
            sb.Append(CultureInfo.InvariantCulture, $"  {stage.Stage,-52}");
            sb.Append(CultureInfo.InvariantCulture, $"{stage.Count,4} x ");
            sb.Append(CultureInfo.InvariantCulture, $"{stage.TotalMs / 1000.0,8:F2} s");
            if (stage.Count > 1)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  (avg {stage.AverageMs:F0} ms, max {stage.MaxMs / 1000.0:F2} s)");
            }

            sb.AppendLine();
        }

        if (stages.Count > shown)
        {
            double rest = stages.Skip(shown).Sum(x => x.TotalMs);
            sb.Append(CultureInfo.InvariantCulture, $"  {"(remaining stages)",-52}{stages.Count - shown,4} x {rest / 1000.0,8:F2} s");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>A running stopwatch that records its stage when disposed.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly string _stage;
        private readonly long _start;

        internal Scope(string stage)
        {
            _stage = stage;
            _start = Stopwatch.GetTimestamp();
        }

        public void Dispose() =>
            Record(_stage, Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
    }
}
