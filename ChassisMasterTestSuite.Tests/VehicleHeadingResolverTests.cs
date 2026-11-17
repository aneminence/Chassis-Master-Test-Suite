using System.Collections;
using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;

namespace ChassisMasterTestSuite.Tests;

public class VehicleHeadingResolverTests
{
    [Theory]
    [InlineData("Head_imu2")]
    [InlineData("Head_imu")]
    [InlineData("True_Head2")]
    [InlineData("True_Head")]
    public void BodyHeading_OverridesCourseEvenAtRest(string channel)
    {
        var sample = Sample(0, 0, 17, (channel, -90));
        Assert.Equal(270, new VehicleHeadingResolver().Resolve(sample, new[] { sample }, 0));
    }

    [Fact]
    public void Ncom_UsesValidInsHeadingAtRest_AndDoesNotReplaceNaNWithTypedZero()
    {
        var valid = Sample(0, 0, -30, ("nav_status", 4), ("pitch_deg", 0), ("roll_deg", 0));
        var invalid = Sample(1, 0, double.NaN, ("nav_status", 0), ("pitch_deg", double.NaN), ("roll_deg", double.NaN));
        var samples = new[] { valid, invalid };
        var resolver = new VehicleHeadingResolver();
        Assert.Equal(330, resolver.Resolve(valid, samples, 0));
        Assert.Equal(330, resolver.Resolve(invalid, samples, 1));
    }

    [Fact]
    public void InvalidBodyHeading_UsesMovingCourse_AndAmbiguousRobotChannelIsNotTrusted()
    {
        var sample = Sample(0, 30, 80, ("True_Head", double.NaN), ("Head_imu2", 999), ("RobotHead", 270));
        Assert.Equal(80, new VehicleHeadingResolver().Resolve(sample, new[] { sample }, 0));
    }

    [Fact]
    public void Stop_HoldsLastMovingCourse_DespiteCourseAndPositionNoise_ThenResumes()
    {
        var samples = new[] { Sample(0, 20, 75), Sample(1, 0.2, 180), Sample(2, 0, 2), Sample(3, 8, 200) };
        var resolver = new VehicleHeadingResolver();
        Assert.Equal(75, resolver.Resolve(samples[0], samples, 0));
        Assert.Equal(75, resolver.Resolve(samples[1], samples, 1));
        Assert.Equal(75, resolver.Resolve(samples[2], samples, 2));
        Assert.Equal(200, resolver.Resolve(samples[3], samples, 3));
    }

    [Fact]
    public void SeekAndCompare_UseEachTracksPreStopCourse_RegardlessOfQueryOrder()
    {
        var a = new[] { Sample(0, 10, 350), Sample(1, 0, 20), Sample(2, 0, 300), Sample(3, 10, 90), Sample(4, 0, 270) };
        var b = new[] { Sample(0, 10, 40), Sample(1, 0, 220) };
        var resolver = new VehicleHeadingResolver();
        Assert.Equal(90, resolver.Resolve(a[4], a, 4));
        Assert.Equal(350, resolver.Resolve(a[2], a, 2));
        Assert.Equal(40, resolver.Resolve(b[1], b, 1));
        Assert.Equal(350, resolver.Resolve(a[1], a, 1));
        Assert.Equal(90, resolver.Resolve(a[4], a, 4));
    }

    [Fact]
    public void InitiallyStationary_UsesOneInitialCourse_AndNeverLooksAheadToDeparture()
    {
        var samples = new[] { Sample(0, 0, 15), Sample(1, 0.1, 270), Sample(2, 10, 90) };
        Assert.Equal(15, new VehicleHeadingResolver().Resolve(samples[1], samples, 1));
    }

    [Fact]
    public void LongStop_FirstSeekScansOnce_SubsequentTicksAndReverseSeekUseSparseCache()
    {
        var data = Enumerable.Range(0, 20_001).Select(i => Sample(i, i == 0 ? 15 : 0, i == 0 ? 123 : i % 360)).ToList();
        var samples = new CountingSamples(data);
        var resolver = new VehicleHeadingResolver();
        Assert.Equal(123, resolver.Resolve(data[10_000], samples, 10_000));
        samples.Reads = 0;
        for (var i = 10_001; i < 10_101; i++)
            Assert.Equal(123, resolver.Resolve(data[i], samples, i));
        Assert.Equal(123, resolver.Resolve(data[5_000], samples, 5_000));
        Assert.InRange(samples.Reads, 0, 700);
    }

    [Fact]
    public void ReplacedTrackContents_InvalidateHeldCourse()
    {
        var samples = new List<VehicleSample> { Sample(0, 10, 75), Sample(1, 0, 120) };
        var resolver = new VehicleHeadingResolver();
        Assert.Equal(75, resolver.Resolve(samples[1], samples, 1));
        samples[0] = Sample(0, 10, 200);
        Assert.Equal(200, resolver.Resolve(samples[1], samples, 1));
    }

    [Fact]
    public void MissingHeading_UsesTrackBearing_AndAnExplicitNorthCourseRemainsValid()
    {
        var samples = new[] { Sample(0, 20, 0, (VehicleHeadingResolver.MissingHeadingKey, 1)), Sample(1, 20, 0) };
        var resolver = new VehicleHeadingResolver();
        Assert.Equal(90, resolver.Resolve(samples[0], samples, 0), 5);
        Assert.Equal(0, resolver.Resolve(samples[1], samples, 1));
    }

    [Fact]
    public void KmlWithoutSpeed_StillFollowsTurns_AndHoldsDuringStop()
    {
        var points = new[]
        {
            new KmlTrackPoint { Longitude = 110, Latitude = 30 },
            new KmlTrackPoint { Longitude = 110.0001, Latitude = 30 },
            new KmlTrackPoint { Longitude = 110.0001, Latitude = 30.0001 },
            new KmlTrackPoint { Longitude = 110.0001, Latitude = 30.0001 },
            new KmlTrackPoint { Longitude = 110.0001, Latitude = 30.0001 },
            new KmlTrackPoint { Longitude = 110.0001, Latitude = 30.0001 }
        };
        var samples = SlopeEnricher.EnrichAll(SampleEnricher.EnrichAll(
            KmlTrackImporter.ToVehicleSamples(new KmlTrack { Name = "No speed", Points = points })));
        var resolver = new VehicleHeadingResolver();
        Assert.Equal(90, resolver.Resolve(samples[1], samples, 1), 5);
        Assert.Equal(0, resolver.Resolve(samples[2], samples, 2), 5);
        Assert.Equal(0, resolver.Resolve(samples[5], samples, 5), 5);
    }

    [Fact]
    public void KmlSparseFixesWithoutSpeed_UsesAdjacentMotionAcrossFiveSecondGaps()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_000);
        var points = new[]
        {
            new KmlTrackPoint { Longitude = 110, Latitude = 30, TimeUtc = start },
            new KmlTrackPoint { Longitude = 110.0001, Latitude = 30, TimeUtc = start.AddSeconds(5) },
            new KmlTrackPoint { Longitude = 110.0001, Latitude = 30.0001, TimeUtc = start.AddSeconds(10) },
            new KmlTrackPoint { Longitude = 110.0001, Latitude = 30.0001, TimeUtc = start.AddSeconds(15) }
        };
        var samples = KmlTrackImporter.ToVehicleSamples(new KmlTrack { Name = "Sparse", Points = points });
        var resolver = new VehicleHeadingResolver();
        Assert.Equal(90, resolver.Resolve(samples[1], samples, 1), 5);
        Assert.Equal(0, resolver.Resolve(samples[2], samples, 2), 5);
        Assert.Equal(0, resolver.Resolve(samples[3], samples, 3), 5);
    }

    [Fact]
    public void VboMissingHeading_KeepsLegacyZero_ButResolverUsesGpsBearing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heading-{Guid.NewGuid():N}.vbo");
        try
        {
            File.WriteAllText(path, "[column names]\ntime lat long velocity\n[data]\n120000 1800 -6600 20\n120001 1800 -6600.006 20\n");
            var samples = SlopeEnricher.EnrichAll(SampleEnricher.EnrichAll(new VboReader(path).ReadAll()));
            Assert.Equal(2, samples.Count);
            Assert.Equal(0, samples[0].Heading);
            Assert.True(samples[0].Channels.ContainsKey(VehicleHeadingResolver.MissingHeadingKey));
            Assert.Equal(90, new VehicleHeadingResolver().Resolve(samples[0], samples, 0), 5);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ImportProvenance_IsNotShownAsSelectableChannel_AndDoesNotHideOtherNames()
    {
        var registry = (ChannelRegistry)Activator.CreateInstance(typeof(ChannelRegistry), nonPublic: true)!;
        try
        {
            registry.SetFromVboColumns(new[] { VehicleHeadingResolver.MissingSpeedKey, VehicleHeadingResolver.MissingHeadingKey,
                "cmts_internal_other", ChannelIds.Heading });
            Assert.False(registry.IsAvailable(VehicleHeadingResolver.MissingSpeedKey));
            Assert.False(registry.IsAvailable(VehicleHeadingResolver.MissingHeadingKey));
            Assert.True(registry.IsAvailable("cmts_internal_other"));
            Assert.True(registry.IsAvailable(ChannelIds.Heading));
        }
        finally { registry.SetLiveCore(); }
    }

    private static VehicleSample Sample(int index, double speed, double heading, params (string Name, double Value)[] extras)
    {
        var channels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        { [ChannelIds.Velocity] = speed, [ChannelIds.Heading] = heading };
        foreach (var (name, value) in extras) channels[name] = value;
        return new VehicleSample { Timestamp = index * 1000, Sequence = index, SpeedKph = speed,
            Heading = double.IsFinite(heading) ? heading : 0,
            Longitude = 110 + index * 0.00001, Latitude = 30, Channels = channels };
    }

    private sealed class CountingSamples(IReadOnlyList<VehicleSample> samples) : IReadOnlyList<VehicleSample>
    {
        public int Reads { get; set; }
        public int Count => samples.Count;
        public VehicleSample this[int index] { get { Reads++; return samples[index]; } }
        public IEnumerator<VehicleSample> GetEnumerator() => samples.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
