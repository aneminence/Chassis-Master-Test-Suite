using System.IO;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;

namespace Chassis_Master_Test_Suite.Session;

/// <summary>
/// 录制状态机。
///
/// Stopped -> Recording <-> Paused -> Stopped
/// </summary>
public enum RecordingState
{
    Stopped,
    Recording,
    Paused
}

/// <summary>
/// 拥有 <see cref="VboRecorder"/> 生命周期与丢样计数。
/// UI 按钮外观 / Elapsed 计时器仍由 MainWindow 负责。
/// </summary>
public sealed class RecordingSession : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<VboRecorder> _createRecorder;
    private VboRecorder? _recorder;
    private bool _disposed;
    private RecordingState _state;
    private long? _recordingStartTimestamp;
    private Task _stopTask = Task.CompletedTask;

    public RecordingSession() : this(() => CreateDefaultRecorder(
        Path.Combine(AppContext.BaseDirectory, "Recordings"), DateTime.Now)) { }

    internal RecordingSession(Func<VboRecorder> createRecorder) =>
        _createRecorder = createRecorder ?? throw new ArgumentNullException(nameof(createRecorder));

    internal static VboRecorder CreateDefaultRecorder(string directory, DateTime now)
    {
        Directory.CreateDirectory(directory);
        var session = SessionMetadata.Current.Clone();
        var stem = $"CMTS_{now:yyyyMMdd_HHmmss}";
        for (var suffix = 1; ; suffix++)
        {
            var path = Path.Combine(directory, suffix == 1 ? $"{stem}.vbo" : $"{stem}_{suffix}.vbo");
            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (File.Exists(path))
            {
                continue;
            }

            try
            {
                return new VboRecorder(path, session, stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
    }

    public RecordingState State { get { lock (_gate) return _state; } }

    /// <summary>
    /// 本次录制会话第一条写入样本的时间戳（毫秒），用于 Elapsed UI。
    /// </summary>
    public long? RecordingStartTimestamp { get { lock (_gate) return _recordingStartTimestamp; } }

    public long DroppedSamples { get { lock (_gate) return _recorder?.DroppedSamples ?? 0; } }

    public string? CurrentFilePath { get { lock (_gate) return _recorder?.FilePath; } }

    /// <summary>Observe active writer failures as well as stop/close failures.</summary>
    public Task Completion { get { lock (_gate) return _recorder?.Completion ?? _stopTask; } }

    public bool IsRecording => State == RecordingState.Recording;

    /// <summary>
    /// 应用录制侧状态：创建/关闭文件、维护时间原点。不碰 UI。
    /// </summary>
    public void ApplyState(RecordingState state)
    {
        if (state == RecordingState.Stopped)
        {
            StopAsync().GetAwaiter().GetResult();
            return;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_stopTask.IsCompleted)
                throw new InvalidOperationException("The previous recording is still closing.");

            if (state == RecordingState.Recording && _recorder is null)
            {
                _recorder = _createRecorder();
                _stopTask = Task.CompletedTask;
                _recordingStartTimestamp = null;
            }

            _state = state;
        }
    }

    public async Task ApplyStateAsync(RecordingState state)
    {
        if (state == RecordingState.Stopped)
        {
            await StopAsync().ConfigureAwait(false);
            return;
        }

        Task stopping;
        lock (_gate) stopping = _stopTask;
        if (!stopping.IsCompleted)
            await stopping.ConfigureAwait(false);
        ApplyState(state);
    }

    public Task StopAsync()
    {
        lock (_gate)
            return StopUnlocked();
    }

    private Task StopUnlocked()
    {
        // Detach atomically with the producer's state check/enqueue so all
        // accepted rows belong to the old writer and no later rows slip in.
        _state = RecordingState.Stopped;
        _recordingStartTimestamp = null;
        if (_recorder is not null)
        {
            _stopTask = _recorder.StopAsync();
            _recorder = null;
        }
        return _stopTask;
    }

    /// <summary>
    /// 仅在 <see cref="RecordingState.Recording"/> 时写入；
    /// 首样本锁定 <see cref="RecordingStartTimestamp"/>。
    /// TryWrite 失败会计入 <see cref="DroppedSamples"/>。
    /// </summary>
    public void TryWriteIfRecording(VehicleSample sample)
    {
        lock (_gate)
        {
            if (_disposed || _state != RecordingState.Recording)
                return;

            _recordingStartTimestamp ??= sample.Timestamp;
            _recorder?.TryWrite(sample);
        }
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            return new ValueTask(StopUnlocked());
        }
    }
}
