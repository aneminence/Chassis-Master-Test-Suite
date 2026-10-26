using Chassis_Master_Test_Suite.Analysis;

namespace ChassisMasterTestSuite.Tests;

public sealed class KmlTrackCutterTests
{
    private static KmlTrack Track() => new()
    {
        Name = "demo",
        Points = Enumerable.Range(0, 6).Select(i => new KmlTrackPoint
        {
            Longitude = 100 + i,
            Latitude = 30 + i,
            AltitudeMeters = i,
            TimeUtc = DateTimeOffset.UnixEpoch.AddSeconds(i)
        }).ToList()
    };

    [Fact]
    public void BeforeCursor_KeepsCursorAndFollowingPoints()
    {
        var source = Track();
        var result = KmlTrackCutter.Cut(source, KmlCutMode.BeforeCursor, 2);
        Assert.Equal(new[] { 102d, 103d, 104d, 105d }, result.Points.Select(p => p.Longitude));
        Assert.Equal(6, source.Points.Count);
    }

    [Fact]
    public void AfterCursor_KeepsCursorAndPreviousPoints()
    {
        var result = KmlTrackCutter.Cut(Track(), KmlCutMode.AfterCursor, 3);
        Assert.Equal(new[] { 100d, 101d, 102d, 103d }, result.Points.Select(p => p.Longitude));
    }

    [Fact]
    public void EditSession_CutBefore_RebasesAndRemovesMarks()
    {
        var session = new KmlEditSession(Track());
        session.AddMark(1);
        var selected = session.AddMark(3);

        session.Cut(KmlCutMode.BeforeCursor);

        Assert.True(session.IsDirty);
        Assert.Equal(new[] { 103d, 104d, 105d }, session.WorkingTrack.Points.Select(p => p.Longitude));
        var remaining = Assert.Single(session.Marks);
        Assert.Equal(selected.Id, remaining.Id);
        Assert.Equal(0, remaining.SampleIndex);
    }

    [Fact]
    public void EditSession_CutAfter_RemovesMarksBeyondCut()
    {
        var session = new KmlEditSession(Track());
        var selected = session.AddMark(2);
        session.AddMark(5);
        session.SelectMark(selected.Id);

        session.Cut(KmlCutMode.AfterCursor);

        Assert.Equal(new[] { 100d, 101d, 102d }, session.WorkingTrack.Points.Select(p => p.Longitude));
        Assert.Single(session.Marks);
        Assert.Equal(selected.Id, session.SelectedMarkId);
    }

    [Fact]
    public void EditSession_MultipleCuts_AreCumulative()
    {
        var session = new KmlEditSession(Track());
        session.AddMark(1);
        session.Cut(KmlCutMode.BeforeCursor);
        session.AddMark(2);
        session.Cut(KmlCutMode.AfterCursor);

        Assert.Equal(new[] { 101d, 102d, 103d }, session.WorkingTrack.Points.Select(p => p.Longitude));
    }

    [Fact]
    public void AddMark_DoesNotDirtyTrack()
    {
        var session = new KmlEditSession(Track());
        session.AddMark(2);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void ExportedTrack_CanBeImportedAgain()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cmts-cut-{Guid.NewGuid():N}.kml");
        try
        {
            var cut = KmlTrackCutter.Cut(Track(), KmlCutMode.BeforeCursor, 2);
            KmlTrackCutter.ExportFile(cut, path);
            var imported = KmlTrackImporter.ImportFile(path);
            Assert.Equal(4, imported.Points.Count);
            Assert.Equal(102, imported.Points[0].Longitude);
            Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(2), imported.Points[0].TimeUtc);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
