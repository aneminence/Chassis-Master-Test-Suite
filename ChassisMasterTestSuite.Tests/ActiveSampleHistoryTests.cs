using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Session;

namespace ChassisMasterTestSuite.Tests;

public class ActiveSampleHistoryTests
{
    [Fact]
    public void OfflineLongFile_PlotsRetainFirstSegmentAndOriginalMapSampleReferences()
    {
        const int capacity = 1_000_000;
        var samples = new VehicleSample[capacity + 17];
        Array.Fill(samples, Sample(500_000));
        samples[0] = Sample(0);
        samples[1] = Sample(1);
        samples[2] = Sample(2);
        samples[^1] = Sample(samples.Length - 1);
        var files = new OfflineFileSet();
        var file = files.Add("long.ncom", samples, Array.Empty<string>());
        var live = new SampleHistoryBuffer(capacity);
        live.AddRange(samples); // Reproduce the former offline-to-live-buffer truncation.
        Assert.Equal(capacity, live.Count);
        Assert.NotSame(samples[0], live.Snapshot()[0]);

        var history = ActiveSampleHistory.GetSnapshot(true, files, live);

        Assert.Same(file.Samples, history);
        Assert.Equal(samples.Length, history.Count);
        Assert.Same(samples[0], history[0]);
        Assert.Same(samples[^1], history[^1]);
        var (start, end) = PlotDownsampler.FindVisibleIndexRange(
            history.Count, i => history[i].Timestamp, 0, 20, useFullRange: false);
        var indices = PlotDownsampler.BuildDownsampleIndices(
            start, end, i => history[i].SpeedKph, PlotDownsampler.MaxPlotPoints);
        var plotSamples = PlotDownsampler.ExtractSamples(history, indices);
        Assert.Contains(samples[0], plotSamples);
        Assert.Contains(samples[2], plotSamples);
        Assert.Same(file.Samples[0], plotSamples[0]);

        var allIndices = PlotDownsampler.BuildDownsampleIndices(
            0, history.Count, i => history[i].SpeedKph, PlotDownsampler.MaxPlotPoints);
        Assert.Equal(0, allIndices[0]);
        Assert.Equal(samples.Length - 1, allIndices[^1]);
    }

    [Fact]
    public void OfflineSelectionAndReplacement_FollowPrimaryWithoutStaleSnapshot()
    {
        var files = new OfflineFileSet();
        var first = files.Add("first.ncom", new[] { Sample(1), Sample(2) }, Array.Empty<string>());
        var second = files.Add("second.ncom", new[] { Sample(3), Sample(4) }, Array.Empty<string>());
        var live = new SampleHistoryBuffer(2);
        Assert.Same(second.Samples, ActiveSampleHistory.GetSnapshot(true, files, live));

        files.Select(first.Id);
        Assert.Same(first.Samples, ActiveSampleHistory.GetSnapshot(true, files, live));

        var replacement = new[] { Sample(10), Sample(11), Sample(12) };
        files.UpdateData(first.Id, replacement, Array.Empty<string>());
        Assert.Same(replacement, ActiveSampleHistory.GetSnapshot(true, files, live));

        files.Remove(first.Id);
        Assert.Same(second.Samples, ActiveSampleHistory.GetSnapshot(true, files, live));
    }

    [Fact]
    public void OfflineWithoutSamples_DoesNotExposeRetainedLiveHistory()
    {
        var files = new OfflineFileSet();
        var live = new SampleHistoryBuffer(2);
        live.Add(Sample(99));
        Assert.Empty(ActiveSampleHistory.GetSnapshot(true, files, live));

        files.Add("empty.ncom", Array.Empty<VehicleSample>(), Array.Empty<string>());
        Assert.Empty(ActiveSampleHistory.GetSnapshot(true, files, live));
    }

    [Fact]
    public void OnlineWithPreservedOfflineFiles_RemainsBoundedAndSnapshotsStayStable()
    {
        var files = new OfflineFileSet();
        var offline = files.Add("saved.ncom", new[] { Sample(100) }, Array.Empty<string>());
        var live = new SampleHistoryBuffer(3);
        live.AddRange(new[] { Sample(1), Sample(2), Sample(3), Sample(4) });

        var first = ActiveSampleHistory.GetSnapshot(false, files, live);
        Assert.Equal(new long[] { 2, 3, 4 }, first.Select(s => s.Sequence));
        Assert.Same(first, ActiveSampleHistory.GetSnapshot(false, files, live));
        Assert.Same(offline.Samples, ActiveSampleHistory.GetSnapshot(true, files, live));

        live.Add(Sample(5));
        Assert.Equal(new long[] { 3, 4, 5 },
            ActiveSampleHistory.GetSnapshot(false, files, live).Select(s => s.Sequence));
        Assert.Equal(new long[] { 2, 3, 4 }, first.Select(s => s.Sequence));
    }

    private static VehicleSample Sample(int sequence) => new()
    {
        Sequence = sequence,
        Timestamp = sequence * 10L,
        SpeedKph = sequence,
        Latitude = 31 + sequence * 1e-8,
        Longitude = 121
    };
}
