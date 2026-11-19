using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class TestResultsLiveMergeTests
{
    [Fact]
    public void TakeNewCompletedRuns_skips_history_at_or_before_cutoff()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var computed = new[]
        {
            Annotate(Result(1, 1000, 2000)),
            Annotate(Result(2, 3000, 4000)),
        };

        var added = TestResultsLiveMerge.TakeNewCompletedRuns(computed, seen, cutoffEndMs: 2000);

        Assert.Single(added);
        Assert.Equal(2, added[0].Result.RunNumber);
        Assert.Single(seen);
    }

    [Fact]
    public void TakeNewCompletedRuns_does_not_repeat_a_seen_run()
    {
        var first = Annotate(Result(1, 1000, 5000));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var once = TestResultsLiveMerge.TakeNewCompletedRuns(new[] { first }, seen, cutoffEndMs: 0);
        var again = TestResultsLiveMerge.TakeNewCompletedRuns(new[] { first }, seen, cutoffEndMs: 0);

        Assert.Single(once);
        Assert.Empty(again);
    }

    [Fact]
    public void MaxSampleTimestampMs_is_latest_across_sources()
    {
        var sources = new[]
        {
            new SampleSource { Label = "A", Samples = new[] { Sample(10), Sample(40) } },
            new SampleSource { Label = "B", Samples = new[] { Sample(70) } },
        };

        Assert.Equal(70, TestResultsLiveMerge.MaxSampleTimestampMs(sources));
    }

    [Fact]
    public void SameConditions_detects_threshold_changes()
    {
        var accel = TestDefinition.Accel(0, 50);
        Assert.True(TestResultsLiveMerge.SameConditions(accel, TestDefinition.Accel(0, 50)));
        Assert.False(TestResultsLiveMerge.SameConditions(accel, TestDefinition.Accel(0, 100)));
        Assert.False(TestResultsLiveMerge.SameConditions(accel, TestDefinition.Decel(80, 0)));
    }

    private static VehicleSample Sample(long timestampMs) =>
        new() { Timestamp = timestampMs, SpeedKph = 0 };

    private static TestRunResult Result(int run, long startMs, long endMs) =>
        new()
        {
            RunNumber = run,
            SourceFile = "live",
            StartTimestampMs = startMs,
            EndTimestampMs = endMs,
            DurationSeconds = (endMs - startMs) / 1000.0,
            Pass = true
        };

    private static AnnotatedTestRun Annotate(TestRunResult result) =>
        new()
        {
            Result = result,
            Samples = Array.Empty<VehicleSample>(),
            SourceLabel = result.SourceFile
        };
}
