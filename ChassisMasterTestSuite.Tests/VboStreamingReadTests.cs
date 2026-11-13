using Chassis_Master_Test_Suite.Recorder;

namespace ChassisMasterTestSuite.Tests;

public class VboStreamingReadTests
{
    [Fact]
    public void EmptyInput_ReturnsEmptyAndReleasesFile()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, string.Empty);
            Assert.Empty(new VboReader(path).ReadAll());
            AssertExclusiveOpen(path);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseFailure_DisposesStreamingEnumeratorAndReleasesFile()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "[column names]\ntime velocity\n[data]\n10000000000000000000 50\n");
            Assert.Throws<ArgumentOutOfRangeException>(() => new VboReader(path).ReadAll());
            AssertExclusiveOpen(path);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void StreamingRead_PreservesSectionsRowOrderDuplicateColumnsAndMissingValues()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path,
                "\uFEFF[comments]\nLog Rate (Hz) : 100.00\n[SessionData]\noperator=review\n" +
                "[column names]\ntime velocity velocity custom custom missing\n[data]\n" +
                "010203.004 12 99 1 2 NaN\n010203.014 13 98 3 4 NaN\n");
            var reader = new VboReader(path);
            var rows = reader.ReadAll();
            Assert.Equal(new long[] { 3_723_004, 3_723_014 }, rows.Select(s => s.Timestamp));
            Assert.Equal(new[] { 12.0, 13.0 }, rows.Select(s => s.SpeedKph));
            Assert.Equal(new[] { 2.0, 4.0 }, rows.Select(s => s.Channels["custom"]));
            Assert.All(rows, row => Assert.True(double.IsNaN(row.Channels["missing"])));
            Assert.Equal(100, reader.LogRateHz);
            Assert.Equal(new[] { "operator=review" }, reader.SessionData);
            Assert.Equal(6, reader.Columns.Count);
            Assert.Equal(0, reader.SkippedDataLines);
            AssertExclusiveOpen(path);
        }
        finally { File.Delete(path); }
    }

    private static void AssertExclusiveOpen(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"cmts-stream-read-{Guid.NewGuid():N}.vbo");
}
