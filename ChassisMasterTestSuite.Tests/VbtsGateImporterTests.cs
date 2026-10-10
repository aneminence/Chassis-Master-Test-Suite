using Chassis_Master_Test_Suite.Analysis;

namespace ChassisMasterTestSuite.Tests;

public class VbtsGateImporterTests
{
    [Theory]
    [InlineData(1995.171897131743, 33.25286495219572)]
    [InlineData(7238.7590545553458, 120.6459842425891)]
    [InlineData(60.0, 1.0)]
    [InlineData(0.0, 0.0)]
    public void MinutesToDegrees_DividesBySixty(double minutes, double expectedDegrees)
    {
        var actual = VbtsGateImporter.MinutesToDegrees(minutes);
        Assert.Equal(expectedDegrees, actual, precision: 9);
    }

    [Fact]
    public void ExtractAndParse_UnescapedGateSettings_Block()
    {
        const string block = """
            <caption>Start</caption>
            <gateWidth>10</gateWidth>
            <heightMetres1>15.43</heightMetres1>
            <heightMetres2>15.43</heightMetres2>
            <key>StartFinishLine_StartFinish</key>
            <latiudeMinutes1>1995.171897131743</latiudeMinutes1>
            <latiudeMinutes2>1995.1719565587491</latiudeMinutes2>
            <longitudeMinutes1>7238.7590545553458</longitudeMinutes1>
            <longitudeMinutes2>7238.7596777576618</longitudeMinutes2>
            """;

        Assert.True(VbtsGateImporter.TryParseGateBlock(block, out var gate));
        Assert.Equal("Start", gate.Name);
        Assert.Equal(10, gate.WidthMeters);
        Assert.Equal(
            VbtsGateImporter.MinutesToDegrees((1995.171897131743 + 1995.1719565587491) / 2),
            gate.Latitude,
            9);
        Assert.Equal(
            VbtsGateImporter.MinutesToDegrees((7238.7590545553458 + 7238.7596777576618) / 2),
            gate.Longitude,
            9);
        Assert.NotNull(gate.HeadingDeg);
        Assert.True(gate.IsValid);
    }

    [Fact]
    public void ImportXml_EscapedNestedPersistenceObject_FindsAllGates()
    {
        const string vbts = """
            <PersistenceObject>
              <pluginsInformation>
                <a:string>&lt;Dockable&gt;&lt;mySetting&gt;&lt;gates&gt;&lt;GateSettings&gt;&lt;caption&gt;SpeedPoint&lt;/caption&gt;&lt;gateWidth&gt;10&lt;/gateWidth&gt;&lt;latiudeMinutes1&gt;1995.1734767924424&lt;/latiudeMinutes1&gt;&lt;latiudeMinutes2&gt;1995.1735789660033&lt;/latiudeMinutes2&gt;&lt;longitudeMinutes1&gt;7238.7806594710992&lt;/longitudeMinutes1&gt;&lt;longitudeMinutes2&gt;7238.7813492933647&lt;/longitudeMinutes2&gt;&lt;/GateSettings&gt;&lt;d3p1:GateSettings&gt;&lt;d3p1:caption&gt;Exit&lt;/d3p1:caption&gt;&lt;d3p1:gateWidth&gt;12.5&lt;/d3p1:gateWidth&gt;&lt;d3p1:latiudeMinutes1&gt;1995.1766838445994&lt;/d3p1:latiudeMinutes1&gt;&lt;d3p1:latiudeMinutes2&gt;1995.1767164284588&lt;/d3p1:latiudeMinutes2&gt;&lt;d3p1:longitudeMinutes1&gt;7238.821960691771&lt;/d3p1:longitudeMinutes1&gt;&lt;d3p1:longitudeMinutes2&gt;7238.8225813067429&lt;/d3p1:longitudeMinutes2&gt;&lt;/d3p1:GateSettings&gt;&lt;/gates&gt;&lt;/mySetting&gt;&lt;/Dockable&gt;</a:string>
              </pluginsInformation>
            </PersistenceObject>
            """;

        var decoded = VbtsGateImporter.DecodeUntilGatesVisible(vbts);
        Assert.Contains("<GateSettings", decoded);
        Assert.DoesNotContain("&lt;GateSettings", decoded);

        var gates = VbtsGateImporter.ImportXml(vbts);
        Assert.Equal(2, gates.Count);
        Assert.Equal("SpeedPoint", gates[0].Name);
        Assert.Equal(10, gates[0].WidthMeters);
        Assert.Equal("Exit", gates[1].Name);
        Assert.Equal(12.5, gates[1].WidthMeters);
        Assert.InRange(gates[0].Latitude, 33.25, 33.26);
        Assert.InRange(gates[0].Longitude, 120.64, 120.65);
    }

    [Fact]
    public void ImportFile_MiniFixture_ReadsCaptionAndDegrees()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mini.vbts");
        Assert.True(File.Exists(path), path);
        var gates = VbtsGateImporter.ImportFile(path);
        Assert.Single(gates);
        Assert.Equal("开始", gates[0].Name);
        Assert.Equal(10, gates[0].WidthMeters);
        Assert.InRange(gates[0].Latitude, 33.25, 33.26);
        Assert.InRange(gates[0].Longitude, 120.64, 120.65);
    }

    [Fact]
    public void BearingDeg_Eastward_IsAboutNinety()
    {
        var b = VbtsGateImporter.BearingDeg(31.0, 121.0, 31.0, 121.001);
        Assert.InRange(b, 85, 95);
    }

    [Fact]
    public void TryParseGateBlock_GateLineIsPerpendicularToEndpointChord()
    {
        // Chord nearly east (~1 m); travel heading = chord bearing => gate bar north-south.
        const string block = """
            <caption>Start</caption>
            <gateWidth>10</gateWidth>
            <latiudeMinutes1>1995.171897131743</latiudeMinutes1>
            <latiudeMinutes2>1995.1719565587491</latiudeMinutes2>
            <longitudeMinutes1>7238.7590545553458</longitudeMinutes1>
            <longitudeMinutes2>7238.7596777576618</longitudeMinutes2>
            """;

        Assert.True(VbtsGateImporter.TryParseGateBlock(block, out var gate));
        Assert.NotNull(gate.HeadingDeg);

        var lat1 = VbtsGateImporter.MinutesToDegrees(1995.171897131743);
        var lat2 = VbtsGateImporter.MinutesToDegrees(1995.1719565587491);
        var lon1 = VbtsGateImporter.MinutesToDegrees(7238.7590545553458);
        var lon2 = VbtsGateImporter.MinutesToDegrees(7238.7596777576618);
        var chordBearing = VbtsGateImporter.BearingDeg(lat1, lon1, lat2, lon2);

        // Stored heading is travel (= chord); gate line is perpendicular to travel.
        Assert.Equal(chordBearing, gate.HeadingDeg!.Value, precision: 3);

        var (gLat0, gLon0, gLat1, gLon1) = GateCrossing.GetGateEndpointsLatLon(gate);
        var gateBearing = VbtsGateImporter.BearingDeg(gLat0, gLon0, gLat1, gLon1);

        var diff = Math.Abs(VbtsGateImporter.NormalizeHeading(gateBearing - chordBearing));
        var undirected = Math.Min(diff, 360.0 - diff);
        undirected = Math.Min(undirected, 180.0 - undirected); // angle between undirected lines
        Assert.InRange(undirected, 80, 100);

        // Width comes from gateWidth, not the short chord.
        Assert.Equal(10, gate.WidthMeters);
        var drawnLen = VbtsGateImporter.DistanceMeters(gLat0, gLon0, gLat1, gLon1);
        Assert.InRange(drawnLen, 9.5, 10.5);
    }

    [Fact]
    public void TryParseGateBlock_EastWestChord_YieldsNorthSouthGateBar()
    {
        // Pure east chord: lat same, lon increases.
        const string block = """
            <caption>EW</caption>
            <gateWidth>20</gateWidth>
            <latiudeMinutes1>1860.0</latiudeMinutes1>
            <latiudeMinutes2>1860.0</latiudeMinutes2>
            <longitudeMinutes1>7260.0</longitudeMinutes1>
            <longitudeMinutes2>7260.06</longitudeMinutes2>
            """;

        Assert.True(VbtsGateImporter.TryParseGateBlock(block, out var gate));
        Assert.InRange(gate.HeadingDeg!.Value, 85, 95); // travel east

        var (lat0, lon0, lat1, lon1) = GateCrossing.GetGateEndpointsLatLon(gate);
        var gateBearing = VbtsGateImporter.BearingDeg(lat0, lon0, lat1, lon1);
        // Undirected gate line should be ~N-S (0 or 180).
        var toNorth = Math.Min(
            Math.Abs(VbtsGateImporter.NormalizeHeading(gateBearing) - 0),
            Math.Abs(VbtsGateImporter.NormalizeHeading(gateBearing) - 180));
        Assert.True(toNorth < 15 || Math.Abs(toNorth - 180) < 15,
            $"Expected N-S gate bar, got bearing {gateBearing}");
        var ns = Math.Min(toNorth, 180 - toNorth);
        Assert.InRange(ns, 0, 15);
    }
}
