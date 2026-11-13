using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class SampleEnricherCapacityTests
{
    [Fact]
    public void WithSynthetic_PreservesOriginalAndOverwritesExistingDerivedValuesCaseInsensitively()
    {
        var original = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["__ELAPSED_TEST__"] = 1000,
            [ChannelIds.DistanceTraveled] = 2000,
            ["Custom"] = double.NaN
        };
        var sample = new VehicleSample
        {
            Timestamp = 123, Sequence = 456, SpeedKph = 78.5,
            VehicleId = "vehicle-1", VehicleLabel = "Review", Channels = original
        };

        var enriched = SampleEnricher.WithSynthetic(sample, 1.25, 42.5);

        Assert.Equal(1000, original["__ELAPSED_TEST__"]);
        Assert.Equal(2000, original[ChannelIds.DistanceTraveled]);
        Assert.NotSame(original, enriched.Channels);
        Assert.Equal(3, enriched.Channels.Count);
        Assert.Equal(1.25, enriched.Channels[ChannelIds.ElapsedTest]);
        Assert.Equal(42.5, enriched.Channels["__DISTANCE_M__"]);
        Assert.True(double.IsNaN(enriched.Channels["custom"]));
        Assert.Equal(sample.Timestamp, enriched.Timestamp);
        Assert.Equal(sample.Sequence, enriched.Sequence);
        Assert.Equal(sample.SpeedKph, enriched.SpeedKph);
        Assert.Equal(sample.VehicleId, enriched.VehicleId);
        Assert.Equal(sample.VehicleLabel, enriched.VehicleLabel);
    }

    [Fact]
    public void WithSynthetic_CaseCollidingSourceKeys_RetainsOriginalDuplicateKeyError()
    {
        var sample = new VehicleSample
        {
            Channels = new Dictionary<string, double>(StringComparer.Ordinal) { ["Custom"] = 1, ["custom"] = 2 }
        };
        Assert.Throws<ArgumentException>(() => SampleEnricher.WithSynthetic(sample, 1, 2));
    }
}
