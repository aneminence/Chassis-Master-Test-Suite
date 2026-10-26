using Chassis_Master_Test_Suite.Recorder.OxTS;

namespace ChassisMasterTestSuite.Tests;

public class XcomImportTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "oxts", name);

    [Fact]
    public void Xcom_Demux_Serial2_Matches_Standalone_Ncom_Bytes()
    {
        var xcomPath = Fixture("mobile-015.xcom");
        var ncomPath = Fixture("mobile-015.ncom");
        Assert.True(File.Exists(xcomPath), "missing xcom fixture");
        Assert.True(File.Exists(ncomPath), "missing ncom fixture");

        var xcom = XcomContainerReader.ReadFile(xcomPath);
        Assert.Equal(3, xcom.Streams.Count);

        var ncomStream = xcom.Streams.Single(s => s.Serial == 2 && s.Kind == XcomStreamKind.Ncom);
        Assert.Equal(XcomStreamKind.Ncom, ncomStream.Kind);

        var demux = new List<byte>();
        foreach (var pkt in ncomStream.DataPackets)
            demux.AddRange(pkt.Payload);

        var standalone = File.ReadAllBytes(ncomPath);
        Assert.Equal(standalone.Length, demux.Count);
        Assert.True(standalone.AsSpan().SequenceEqual(demux.ToArray()));
    }

    [Fact]
    public void NcomDecoder_FirstPackets_Match_Expected_LatLon()
    {
        var ncomPath = Fixture("mobile-015.ncom");
        var bytes = File.ReadAllBytes(ncomPath);
        var all = NcomDecoder.DecodeAll(bytes);
        Assert.True(all.Count >= 5);

        var m0 = all[0];
        Assert.Equal(4, m0.NavigationStatus);
        Assert.InRange(m0.LatitudeDeg, 42.6193, 42.6195);
        Assert.InRange(m0.LongitudeDeg, 91.5222, 91.5224);
        Assert.InRange(m0.AltitudeM, 469.0, 469.2);
        Assert.True(m0.SpeedHorizontalMps > 20);
    }

    [Fact]
    public void XcomImporter_Produces_Separate_Ncom_Streams_And_Rcom()
    {
        var xcomPath = Fixture("mobile-015.xcom");
        var result = XcomImporter.ImportFile(xcomPath);

        var ncomTracks = result.Tracks.Where(t => t.Kind == XcomStreamKind.Ncom).ToList();
        var rcomTracks = result.Tracks.Where(t => t.Kind == XcomStreamKind.Rcom).ToList();

        Assert.Equal(2, ncomTracks.Count);
        Assert.Single(rcomTracks);

        var hunter = ncomTracks.Single(t => t.StreamSerial == 2);
        Assert.True(hunter.Samples.Count >= 1000);
        Assert.Contains(hunter.ColumnNames, c => c.Equals("lat", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hunter.Samples, s => Math.Abs(s.Latitude) > 1);

        var rcom = rcomTracks[0];
        Assert.True(rcom.Samples.Count >= 100);
        Assert.Contains(rcom.ColumnNames, c => c.Equals("range_long_m", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RcomDecoder_Counts_Extended_Range_On_Demuxed_Stream()
    {
        var xcom = XcomContainerReader.ReadFile(Fixture("mobile-015.xcom"));
        var rcom = xcom.Streams.Single(s => s.Kind == XcomStreamKind.Rcom);
        var payloads = rcom.DataPackets.Select(p => p.Payload).ToList();
        var decoded = RcomDecoder.DecodeFramedPackets(payloads);
        Assert.Equal(2077, decoded.ExtendedRanges.Count);
        Assert.True(decoded.SkippedTypeCounts.ContainsKey(RcomDecoder.TypeLane));
    }
}
