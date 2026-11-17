using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;
using Chassis_Master_Test_Suite.Session;

namespace ChassisMasterTestSuite.Tests;

public class MultiDeviceRecordingSessionTests
{
    [Fact]
    public async Task StartTwoDevices_RoutesSamplesToMatchingFiles()
    {
        var directory = NewDirectory();
        try
        {
            await using var session = new MultiDeviceRecordingSession(directory);
            var now = new DateTime(2026, 11, 17, 17, 45, 1);
            var paths = session.Start(
                new (string, string?)[] { ("phone-a", "Car A"), ("phone-b", "Car B") },
                now);

            Assert.True(session.IsRecording);
            Assert.Equal(2, paths.Count);
            Assert.Contains(paths, path => Path.GetFileName(path) == "CMTS_20261117_174501_Car_A_phone-a.vbo");
            Assert.Contains(paths, path => Path.GetFileName(path) == "CMTS_20261117_174501_Car_B_phone-b.vbo");

            session.TryWrite(Sample("phone-a", 10));
            session.TryWrite(Sample("phone-b", 20));
            session.TryWrite(Sample("phone-a", 11));
            session.TryWrite(Sample("phone-b", 21));

            var closed = await session.StopAsync();
            Assert.False(session.IsRecording);
            Assert.Equal(2, closed.Count);

            var speedsA = ReadSpeeds(paths.Single(path => path.Contains("phone-a", StringComparison.Ordinal)));
            var speedsB = ReadSpeeds(paths.Single(path => path.Contains("phone-b", StringComparison.Ordinal)));
            Assert.Equal(new[] { 10.0, 11.0 }, speedsA);
            Assert.Equal(new[] { 20.0, 21.0 }, speedsB);
        }
        finally { Cleanup(directory); }
    }

    [Fact]
    public async Task MidSessionEnsure_OpensThirdFile_WithoutChangingExisting()
    {
        var directory = NewDirectory();
        try
        {
            await using var session = new MultiDeviceRecordingSession(directory);
            var start = new DateTime(2026, 11, 17, 17, 45, 2);
            session.Start(new (string, string?)[] { ("phone-a", "A"), ("phone-b", "B") }, start);
            session.TryWrite(Sample("phone-a", 1));
            session.TryWrite(Sample("phone-b", 2));

            var added = new DateTime(2026, 11, 17, 17, 46, 0);
            var third = session.EnsureDevice("phone-c", "C", added);
            Assert.Equal("CMTS_20261117_174600_C_phone-c.vbo", Path.GetFileName(third));
            session.TryWrite(Sample("phone-c", 3));
            session.TryWrite(Sample("phone-a", 4));

            var originalA = session.CurrentFilePaths.Single(path => path.Contains("phone-a", StringComparison.Ordinal));
            await session.StopAsync();

            Assert.Equal("CMTS_20261117_174502_A_phone-a.vbo", Path.GetFileName(originalA));
            Assert.Equal(new[] { 1.0, 4.0 }, ReadSpeeds(originalA));
            Assert.Equal(new[] { 2.0 }, ReadSpeeds(Path.Combine(directory, "CMTS_20261117_174502_B_phone-b.vbo")));
            Assert.Equal(new[] { 3.0 }, ReadSpeeds(third!));
        }
        finally { Cleanup(directory); }
    }

    [Fact]
    public async Task StopDevice_ClosesOnlyThatFile_AndReconnectStartsNewFile()
    {
        var directory = NewDirectory();
        try
        {
            await using var session = new MultiDeviceRecordingSession(directory);
            var start = new DateTime(2026, 11, 17, 17, 45, 3);
            session.Start(new (string, string?)[] { ("phone-a", "A"), ("phone-b", "B") }, start);
            session.TryWrite(Sample("phone-a", 1));
            session.TryWrite(Sample("phone-b", 2));

            var closedA = await session.StopDeviceAsync("phone-a");
            Assert.Equal("CMTS_20261117_174503_A_phone-a.vbo", Path.GetFileName(closedA));
            var filesAfterClose = Directory.GetFiles(directory).Length;
            session.TryWrite(Sample("phone-a", 99));
            session.TryWrite(Sample("phone-b", 3));
            Assert.Equal(filesAfterClose, Directory.GetFiles(directory).Length);
            Assert.Equal(new[] { 1.0 }, ReadSpeeds(closedA!));

            var reopened = session.EnsureDevice("phone-a", "A-renamed", new DateTime(2026, 11, 17, 17, 45, 9));
            Assert.NotEqual(closedA, reopened);
            Assert.Equal("CMTS_20261117_174509_A-renamed_phone-a.vbo", Path.GetFileName(reopened));
            session.TryWrite(Sample("phone-a", 4));

            await session.StopAsync();
            Assert.Equal(new[] { 1.0 }, ReadSpeeds(closedA!));
            Assert.Equal(new[] { 4.0 }, ReadSpeeds(reopened!));
            Assert.Equal(new[] { 2.0, 3.0 }, ReadSpeeds(Path.Combine(directory, "CMTS_20261117_174503_B_phone-b.vbo")));
        }
        finally { Cleanup(directory); }
    }

    [Fact]
    public async Task WritesAfterStop_AreIgnored_AndEnsureIsNoOpUntilRestart()
    {
        var directory = NewDirectory();
        try
        {
            await using var session = new MultiDeviceRecordingSession(directory);
            var now = new DateTime(2026, 11, 17, 17, 45, 4);
            var paths = session.Start(new (string, string?)[] { ("phone-a", "A") }, now);
            session.TryWrite(Sample("phone-a", 1));
            await session.StopAsync();

            session.TryWrite(Sample("phone-a", 2));
            Assert.Null(session.EnsureDevice("phone-a", "A", now));
            Assert.False(session.IsRecording);
            Assert.Equal(new[] { 1.0 }, ReadSpeeds(paths[0]));
        }
        finally { Cleanup(directory); }
    }

    [Fact]
    public void BuildFileStem_SanitizesAndKeepsDeviceIdWhenLabelsCollide()
    {
        var now = new DateTime(2026, 11, 17, 18, 0, 0);
        Assert.Equal(
            "CMTS_20261117_180000_phone-1",
            MultiDeviceRecordingSession.BuildFileStem(now, "phone-1", "phone-1"));
        Assert.Equal(
            "CMTS_20261117_180000_Car_A_phone-1",
            MultiDeviceRecordingSession.BuildFileStem(now, "phone-1", @"Car A/\:*?"));
        Assert.Equal(
            "CMTS_20261117_180000_Front_phone-1",
            MultiDeviceRecordingSession.BuildFileStem(now, "phone-1", "Front"));
        Assert.Equal(
            "CMTS_20261117_180000_Front_phone-2",
            MultiDeviceRecordingSession.BuildFileStem(now, "phone-2", "Front"));
    }

    [Fact]
    public async Task CreateNamedRecorder_SameStem_UsesNumericSuffix()
    {
        var directory = NewDirectory();
        try
        {
            var first = RecordingSession.CreateNamedRecorder(directory, "CMTS_20261117_174505_A_phone-a");
            Assert.True(first.TryWrite(Sample("phone-a", 1)));
            await first.StopAsync();

            await using var session = new MultiDeviceRecordingSession(directory);
            var paths = session.Start(
                new (string, string?)[] { ("phone-a", "A") },
                new DateTime(2026, 11, 17, 17, 45, 5));
            session.TryWrite(Sample("phone-a", 2));
            await session.StopAsync();

            Assert.Equal("CMTS_20261117_174505_A_phone-a_2.vbo", Path.GetFileName(paths[0]));
            Assert.Equal(new[] { 1.0 }, ReadSpeeds(first.FilePath));
            Assert.Equal(new[] { 2.0 }, ReadSpeeds(paths[0]));
        }
        finally { Cleanup(directory); }
    }

    [Fact]
    public async Task EnsureDevice_WhileOpen_DoesNotRenameExistingFile()
    {
        var directory = NewDirectory();
        try
        {
            await using var session = new MultiDeviceRecordingSession(directory);
            var now = new DateTime(2026, 11, 17, 17, 45, 6);
            var original = session.Start(new (string, string?)[] { ("phone-a", "Old") }, now).Single();
            var again = session.EnsureDevice("phone-a", "New Name", now);
            Assert.Equal(original, again);
            Assert.Equal("CMTS_20261117_174506_Old_phone-a.vbo", Path.GetFileName(original));
            session.TryWrite(Sample("phone-a", 1));
            await session.StopAsync();
            Assert.Equal(new[] { 1.0 }, ReadSpeeds(original));
        }
        finally { Cleanup(directory); }
    }

    private static string NewDirectory() =>
        Directory.CreateTempSubdirectory("cmts-multi-rec-").FullName;

    private static void Cleanup(string directory)
    {
        foreach (var file in Directory.GetFiles(directory))
            File.Delete(file);
        Directory.Delete(directory);
    }

    private static VehicleSample Sample(string vehicleId, int speed) => new()
    {
        Timestamp = 1_700_000_000_000L + speed * 10L,
        SpeedKph = speed,
        VehicleId = vehicleId,
        VehicleLabel = vehicleId,
        Latitude = 31.23,
        Longitude = 121.47
    };

    private static double[] ReadSpeeds(string path) =>
        new VboReader(path).ReadAll().Select(sample => sample.SpeedKph).ToArray();
}
