using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;
using Chassis_Master_Test_Suite.Session;

namespace ChassisMasterTestSuite.Tests;

public class RecordingFileNameTests
{
    [Fact]
    public async Task DefaultRecorder_RestartInSameSecond_PreservesEarlierFile()
    {
        var directory = NewDirectory();
        try
        {
            var now = new DateTime(2026, 11, 11, 12, 34, 56);
            var first = RecordingSession.CreateDefaultRecorder(directory, now);
            Assert.True(first.TryWrite(Sample(1)));
            await first.StopAsync();
            var original = File.ReadAllBytes(first.FilePath);

            var second = RecordingSession.CreateDefaultRecorder(directory, now);
            Assert.True(second.TryWrite(Sample(2)));
            await second.StopAsync();

            Assert.Equal("CMTS_20261111_123456.vbo", Path.GetFileName(first.FilePath));
            Assert.Equal("CMTS_20261111_123456_2.vbo", Path.GetFileName(second.FilePath));
            Assert.Equal(original, File.ReadAllBytes(first.FilePath));
            Assert.Equal(2, new VboReader(second.FilePath).ReadAll().Single().SpeedKph);
        }
        finally { Cleanup(directory); }
    }

    [Fact]
    public async Task DefaultRecorder_ConcurrentSameSecondCreation_ReservesUniqueFiles()
    {
        var directory = NewDirectory();
        try
        {
            var now = new DateTime(2026, 11, 11, 12, 34, 56);
            var paths = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
            {
                var recorder = RecordingSession.CreateDefaultRecorder(directory, now);
                Assert.True(recorder.TryWrite(Sample(i)));
                await recorder.StopAsync();
                return recorder.FilePath;
            })));

            Assert.Equal(8, paths.Distinct().Count());
            Assert.Equal(Enumerable.Range(0, 8).Select(i => (double)i),
                paths.Select(path => new VboReader(path).ReadAll().Single().SpeedKph).Order());
        }
        finally { Cleanup(directory); }
    }

    [Fact]
    public void ExplicitRecorderPath_RetainsExistingOverwriteContract()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "explicit.vbo");
        try
        {
            File.WriteAllText(path, "old content");
            using (var recorder = new VboRecorder(path))
                Assert.True(recorder.TryWrite(Sample(3)));
            Assert.Equal(3, new VboReader(path).ReadAll().Single().SpeedKph);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Cleanup(directory); }
    }

    private static string NewDirectory() => Directory.CreateTempSubdirectory("cmts-recorder-names-").FullName;

    private static void Cleanup(string directory)
    {
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }

    private static VehicleSample Sample(int i) => new() { Timestamp = 1_700_000_000_000L, SpeedKph = i };
}
