namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// Helpers for live Test Results: append a completed run once, without
/// dumping history that already existed when watching started or after Reset.
/// </summary>
public static class TestResultsLiveMerge
{
    public static string RunKey(TestRunResult result) =>
        $"{result.SourceFileId?.ToString() ?? ""}|{result.SourceFile}|{result.StartTimestampMs}|{result.EndTimestampMs}";

    public static long MaxSampleTimestampMs(IEnumerable<SampleSource> sources)
    {
        long max = 0;
        foreach (var source in sources)
        {
            if (source.Samples is not { Count: > 0 })
                continue;
            var timestamp = source.Samples[^1].Timestamp;
            if (timestamp > max)
                max = timestamp;
        }

        return max;
    }

    public static bool SameConditions(TestDefinition? left, TestDefinition? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        if (left.Type != right.Type)
            return false;
        if (left.Type is TestType.Gate or TestType.LapTime)
            return true;

        return left.Start.ChannelId == right.Start.ChannelId
            && left.End.ChannelId == right.End.ChannelId
            && left.Start.Threshold == right.Start.Threshold
            && left.End.Threshold == right.End.Threshold
            && left.Start.Direction == right.Start.Direction
            && left.End.Direction == right.End.Direction;
    }

    /// <summary>
    /// Returns newly finished runs (end after cutoff) and records their keys in
    /// <paramref name="seenKeys"/> so they are not reported twice.
    /// </summary>
    public static IReadOnlyList<AnnotatedTestRun> TakeNewCompletedRuns(
        IReadOnlyList<AnnotatedTestRun> computed,
        ISet<string> seenKeys,
        long cutoffEndMs)
    {
        var added = new List<AnnotatedTestRun>();
        foreach (var run in computed)
        {
            if (run.Result.EndTimestampMs <= cutoffEndMs)
                continue;
            if (!seenKeys.Add(RunKey(run.Result)))
                continue;
            added.Add(run);
        }

        return added;
    }
}
