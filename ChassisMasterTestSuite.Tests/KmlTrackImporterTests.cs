using Chassis_Master_Test_Suite.Analysis;

namespace ChassisMasterTestSuite.Tests;

public class KmlTrackImporterTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "2bulu-sample.kml");

    [Fact]
    public void Import_2buluSample_Parses5536PointsWithTimeAndSpeed()
    {
        Assert.True(File.Exists(FixturePath), $"Missing fixture: {FixturePath}");

        var track = KmlTrackImporter.ImportFile(FixturePath);

        Assert.Equal("2026-06-04 07:25 驾车", track.Name);
        Assert.Equal(5536, track.Points.Count);

        var first = track.Points[0];
        Assert.Equal(102.4278879, first.Longitude, precision: 7);
        Assert.Equal(39.8015483, first.Latitude, precision: 7);
        Assert.Equal(1147.45, first.AltitudeMeters!.Value, precision: 2);
        Assert.Equal(
            DateTimeOffset.Parse("2026-06-03T23:25:15Z"),
            first.TimeUtc);
        Assert.Equal(7.95, first.SpeedKph!.Value, precision: 2);
        Assert.Equal(8.377493, first.AccuracyMeters!.Value, precision: 5);

        var last = track.Points[^1];
        Assert.Equal(101.6715433, last.Longitude, precision: 7);
        Assert.Equal(39.2074149, last.Latitude, precision: 7);
        Assert.Equal(1452.43, last.AltitudeMeters!.Value, precision: 2);
        Assert.Equal(
            DateTimeOffset.Parse("2026-06-04T08:36:03Z"),
            last.TimeUtc);
        Assert.Equal(0.72, last.SpeedKph!.Value, precision: 2);
        Assert.Equal(6.980179, last.AccuracyMeters!.Value, precision: 5);

        Assert.Equal(5536, track.Points.Count(p => p.SpeedKph is not null));
        Assert.Equal(5536, track.Points.Count(p => p.TimeUtc is not null));
    }

    [Fact]
    public void ToVehicleSamples_MapsLatLonAltSpeedAndTimestamp()
    {
        var track = KmlTrackImporter.ImportFile(FixturePath);
        var samples = KmlTrackImporter.ToVehicleSamples(track);

        Assert.Equal(5536, samples.Count);
        Assert.Equal(track.Points[0].Latitude, samples[0].Latitude);
        Assert.Equal(track.Points[0].Longitude, samples[0].Longitude);
        Assert.Equal(track.Points[0].AltitudeMeters!.Value, samples[0].Altitude);
        Assert.Equal(track.Points[0].SpeedKph!.Value, samples[0].SpeedKph);
        Assert.Equal(
            track.Points[0].TimeUtc!.Value.ToUnixTimeMilliseconds(),
            samples[0].Timestamp);
        Assert.Equal(
            track.Points[^1].TimeUtc!.Value.ToUnixTimeMilliseconds(),
            samples[^1].Timestamp);
    }

    [Fact]
    public void ImportXml_LineString_ParsesCoordinatesWithoutTime()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <kml xmlns="http://www.opengis.net/kml/2.2">
              <Document>
                <name>Line demo</name>
                <Placemark>
                  <LineString>
                    <coordinates>
                      117.2001,39.1001,10
                      117.2005,39.1004,11
                      117.2010,39.1008,12
                    </coordinates>
                  </LineString>
                </Placemark>
              </Document>
            </kml>
            """;

        var track = KmlTrackImporter.ImportXml(xml);
        Assert.Equal("Line demo", track.Name);
        Assert.Equal(3, track.Points.Count);
        Assert.Equal(117.2001, track.Points[0].Longitude, precision: 4);
        Assert.Equal(39.1001, track.Points[0].Latitude, precision: 4);
        Assert.Equal(10, track.Points[0].AltitudeMeters!.Value, precision: 1);
        Assert.Null(track.Points[0].TimeUtc);
        Assert.Null(track.Points[0].SpeedKph);

        var samples = KmlTrackImporter.ToVehicleSamples(track);
        Assert.Equal(0, samples[0].Timestamp);
        Assert.Equal(1000, samples[1].Timestamp);
        Assert.Equal(2000, samples[2].Timestamp);
    }

    [Fact]
    public void ParseGxTrackExtendedData_SplitsSpeedAccuracyPairs()
    {
        var pairs = KmlTrackImporter.ParseGxTrackExtendedData(
            "7.95,8.377493;9.82,3.7900925;0.72,6.980179;");
        Assert.Equal(3, pairs.Count);
        Assert.Equal(7.95, pairs[0].Speed!.Value, precision: 2);
        Assert.Equal(8.377493, pairs[0].Acc!.Value, precision: 5);
        Assert.Equal(0.72, pairs[2].Speed!.Value, precision: 2);
    }
}
