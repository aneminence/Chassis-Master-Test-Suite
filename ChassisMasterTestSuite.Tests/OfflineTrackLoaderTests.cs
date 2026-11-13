using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;

namespace ChassisMasterTestSuite.Tests;

public class OfflineTrackLoaderTests
{
    [Fact]
    public async Task BackgroundKmlLoadPreservesOriginalAndEnrichedData()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "2bulu-sample.kml");
        var original = KmlTrackImporter.ImportFile(path);
        var expected = SlopeEnricher.EnrichAll(SampleEnricher.EnrichAll(
            KmlTrackImporter.ToVehicleSamples(original)));
        var result = await Task.Run(() => OfflineTrackLoader.Load(path));
        var track = Assert.Single(result.Tracks);
        Assert.Equal(original.Name, track.Name);
        Assert.Equal(original.Points.Count, track.Kml!.Points.Count);
        Assert.Equal(expected.Count, track.Samples.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Timestamp, track.Samples[i].Timestamp);
            Assert.Equal(expected[i].Latitude, track.Samples[i].Latitude);
            Assert.Equal(expected[i].Longitude, track.Samples[i].Longitude);
            Assert.Equal(expected[i].SpeedKph, track.Samples[i].SpeedKph);
            Assert.Equal(expected[i].GetChannel(ChannelIds.SlopePct), track.Samples[i].GetChannel(ChannelIds.SlopePct));
        }
    }

    [Fact]
    public async Task BackgroundVboLoadPreservesSampleOrderAndDerivedColumns()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gspot-g4-mini.vbo");
        var expected = SlopeEnricher.EnrichAll(SampleEnricher.EnrichAll(new VboReader(path).ReadAll()));
        var result = await Task.Run(() => OfflineTrackLoader.Load(path));
        var track = Assert.Single(result.Tracks);
        Assert.Equal(expected.Select(s => s.Timestamp), track.Samples.Select(s => s.Timestamp));
        Assert.Equal(expected.Select(s => s.SpeedKph), track.Samples.Select(s => s.SpeedKph));
        Assert.Equal(expected.Select(s => s.Longitude), track.Samples.Select(s => s.Longitude));
        Assert.Contains(ChannelIds.SlopePct, track.Columns);
    }

    [Fact]
    public void CancelledImportDoesNotOpenFiles()
    {
        Assert.Throws<OperationCanceledException>(() =>
            OfflineTrackLoader.Load("missing.vbo", new CancellationToken(true)));
    }
}
