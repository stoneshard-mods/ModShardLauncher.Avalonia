using System.Text.Json;
using ModShardLauncher;

namespace ModShardLauncherTest;

/// <summary>
/// <see cref="PatchTimings"/> is the measurement backbone of the performance work: these tests pin
/// the aggregation, the JSON shape (scripts read it to compare runs) and the summary formatting.
/// </summary>
public class PatchTimingsTests
{
    /// <summary>Stage names are unique per test so a concurrently running suite cannot pollute them.</summary>
    private const string First = "test:a-first";
    private const string Second = "test:b-second";

    [Fact]
    public void Measure_RecordsOneEventPerScope()
    {
        PatchTimings.Reset();

        using (PatchTimings.Measure(First))
        {
            Thread.Sleep(15);
        }

        PatchTimings.StageTiming stage = Assert.Single(
            PatchTimings.Aggregate().Where(x => x.Stage == First));
        Assert.Equal(1, stage.Count);
        Assert.True(stage.TotalMs >= 10, $"expected at least 10 ms, got {stage.TotalMs}");
    }

    [Fact]
    public void Aggregate_GroupsRepeatedStages_AndSortsByTotal()
    {
        PatchTimings.Reset();

        PatchTimings.Record(Second, 10);
        PatchTimings.Record(Second, 30);
        PatchTimings.Record(First, 100);

        IReadOnlyList<PatchTimings.StageTiming> stages = PatchTimings.Aggregate();

        PatchTimings.StageTiming repeated = Assert.Single(stages.Where(x => x.Stage == Second));
        Assert.Equal(2, repeated.Count);
        Assert.Equal(40, repeated.TotalMs);
        Assert.Equal(30, repeated.MaxMs);
        Assert.Equal(20, repeated.AverageMs);

        // The 100 ms stage must be reported before the 40 ms one.
        int firstIndex = stages.ToList().FindIndex(x => x.Stage == First);
        int secondIndex = stages.ToList().FindIndex(x => x.Stage == Second);
        Assert.True(firstIndex < secondIndex, "stages must be ordered by total time, slowest first");
    }

    [Fact]
    public void Reset_DropsPreviousEvents()
    {
        PatchTimings.Record(First, 5);
        Assert.False(PatchTimings.IsEmpty);

        PatchTimings.Reset();

        Assert.True(PatchTimings.IsEmpty);
        Assert.Empty(PatchTimings.Aggregate());
        Assert.Empty(PatchTimings.Snapshot());
    }

    [Fact]
    public void ToJson_IsReadableAndCarriesSchemaStagesAndSequence()
    {
        PatchTimings.Reset();
        PatchTimings.Record(First, 12.5);
        PatchTimings.Record(First, 7.5);

        using JsonDocument document = JsonDocument.Parse(PatchTimings.ToJson());
        JsonElement root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("mslVersion").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("utmtLibVersion").GetString()));

        JsonElement stage = Assert.Single(
            root.GetProperty("stages").EnumerateArray().Where(x => x.GetProperty("stage").GetString() == First));
        Assert.Equal(2, stage.GetProperty("count").GetInt32());
        Assert.Equal(20, stage.GetProperty("totalMs").GetDouble());

        // The ordered sequence must keep both events so a reader can see load → patch → save.
        Assert.Equal(
            2,
            root.GetProperty("sequence").EnumerateArray()
                .Count(x => x.GetProperty("stage").GetString() == First));
    }

    [Fact]
    public void Summaries_MentionTheTopLevelStagesOnlyWhenRecorded()
    {
        PatchTimings.Reset();
        Assert.Contains("nothing recorded", PatchTimings.ToSummary(), StringComparison.OrdinalIgnoreCase);

        PatchTimings.Record("load", 1000);
        PatchTimings.Record("pipeline", 2000);
        PatchTimings.Record("save", 500);

        Assert.Equal("Timings: load 1.0s, patch 2.0s, save 0.5s", PatchTimings.ToCompactSummary());

        string summary = PatchTimings.ToSummary();
        Assert.Contains("load", summary);
        Assert.Contains("pipeline", summary);
        Assert.Contains("save", summary);
    }

    [Fact]
    public void Summary_ReportsTheRemainingStagesAsOneLine()
    {
        PatchTimings.Reset();
        for (int i = 0; i < 20; i++)
        {
            PatchTimings.Record($"test:stage-{i:D2}", 100 - i);
        }

        string summary = PatchTimings.ToSummary(top: 5);
        Assert.Contains("(remaining stages)", summary);
    }
}
