using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;
using Chassis_Master_Test_Suite.Session;

namespace ChassisMasterTestSuite.Tests;

public class VboRecorderLifecycleTests
{
    [Fact]
    public async Task Dispose_WithNonPumpingUiContext_DrainsWithoutPostingToUi()
    {
        await Task.Run(() =>
        {
            var path = TempPath();
            var previous = SynchronizationContext.Current;
            var context = new NonPumpingContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var recorder = new VboRecorder(path);
                for (var i = 0; i < 100; i++)
                    Assert.True(recorder.TryWrite(Sample(i)));

                recorder.Dispose();
                recorder.Dispose();

                Assert.Equal(100, recorder.RowCount);
                Assert.Equal(0, context.PostCount);
                Assert.Equal(100, new VboReader(path).ReadAll().Count);
                // The completion contract includes releasing the file handle.
                using var reopened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
                File.Delete(path);
            }
        }).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StopAsync_PreservesAllAcceptedRowsAndOrder()
    {
        var path = TempPath();
        try
        {
            var recorder = new VboRecorder(path);
            for (var i = 0; i < 5000; i++)
                Assert.True(recorder.TryWrite(Sample(i)));

            var stopping = recorder.StopAsync();
            Assert.Same(stopping, recorder.StopAsync());
            Assert.False(recorder.TryWrite(Sample(5000)));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            await recorder.DisposeAsync();

            var rows = new VboReader(path).ReadAll();
            var writtenTimes = File.ReadLines(path).SkipWhile(line => line != "[data]").Skip(1)
                .Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Split(' ')[0]).ToArray();
            Assert.Equal(5000, rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                Assert.Equal(Sample(i).SpeedKph, rows[i].SpeedKph, precision: 3);
                Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(Sample(i).Timestamp).UtcDateTime
                    .ToString("HHmmss.fff", System.Globalization.CultureInfo.InvariantCulture), writtenTimes[i]);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task WriterFailure_IsObservableAndRejectsFurtherRows()
    {
        var path = TempPath();
        try
        {
            var recorder = new VboRecorder(path);
            Assert.True(recorder.TryWrite(new VehicleSample { Timestamp = long.MaxValue }));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => recorder.Completion.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.False(recorder.TryWrite(Sample(1)));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => recorder.StopAsync());
            Assert.Throws<ArgumentOutOfRangeException>(() => recorder.Dispose());
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RecordingSession_PauseResumeAndStop_PreserveOneFileAndExcludePausedRows()
    {
        var path = TempPath();
        var created = 0;
        try
        {
            await using var session = new RecordingSession(() =>
            {
                created++;
                return new VboRecorder(path);
            });
            session.ApplyState(RecordingState.Recording);
            session.TryWriteIfRecording(Sample(1));
            session.ApplyState(RecordingState.Paused);
            session.TryWriteIfRecording(Sample(2));
            session.ApplyState(RecordingState.Recording);
            session.TryWriteIfRecording(Sample(3));
            var stopping = session.StopAsync();
            Assert.Equal(RecordingState.Stopped, session.State);
            session.TryWriteIfRecording(Sample(4));
            await stopping;

            Assert.Equal(1, created);
            Assert.Null(session.RecordingStartTimestamp);
            Assert.Equal(new[] { 1.0, 3.0 }, new VboReader(path).ReadAll().Select(s => s.SpeedKph));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RecordingSession_AfterReportedFailure_CanStartFreshRecording()
    {
        var failedPath = TempPath();
        var recoveredPath = TempPath();
        var created = 0;
        try
        {
            await using var session = new RecordingSession(() =>
                new VboRecorder(created++ == 0 ? failedPath : recoveredPath));
            await session.ApplyStateAsync(RecordingState.Recording);
            session.TryWriteIfRecording(new VehicleSample { Timestamp = long.MaxValue });
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.StopAsync());

            await session.ApplyStateAsync(RecordingState.Recording);
            session.TryWriteIfRecording(Sample(1));
            await session.StopAsync();

            Assert.Equal(2, created);
            Assert.Single(new VboReader(recoveredPath).ReadAll());
        }
        finally { File.Delete(failedPath); File.Delete(recoveredPath); }
    }

    private sealed class NonPumpingContext : SynchronizationContext
    {
        public int PostCount;
        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref PostCount);
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"cmts-recorder-{Guid.NewGuid():N}.vbo");

    private static VehicleSample Sample(int index) => new()
    {
        Timestamp = 1_700_000_000_000L + index * 10L,
        SpeedKph = index % 200,
        Latitude = 31.23,
        Longitude = 121.47
    };
}
