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
public sealed class RecordingSession : IDisposable
{
    private VboRecorder? _recorder;
    private bool _disposed;

    public RecordingState State { get; private set; } = RecordingState.Stopped;

    /// <summary>
    /// 本次录制会话第一条写入样本的时间戳（毫秒），用于 Elapsed UI。
    /// </summary>
    public long? RecordingStartTimestamp { get; private set; }

    public long DroppedSamples => _recorder?.DroppedSamples ?? 0;

    public string? CurrentFilePath => _recorder?.FilePath;

    public bool IsRecording => State == RecordingState.Recording;

    /// <summary>
    /// 应用录制侧状态：创建/关闭文件、维护时间原点。不碰 UI。
    /// </summary>
    public void ApplyState(RecordingState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        switch (state)
        {
            case RecordingState.Stopped:
                _recorder?.Dispose();
                _recorder = null;
                RecordingStartTimestamp = null;
                break;

            case RecordingState.Recording:
                // 从停止状态开始时才新建文件；
                // 从暂停恢复时继续写同一个文件。
                if (_recorder is null)
                {
                    RecordingStartTimestamp = null;
                    StartRecorder();
                }

                break;

            case RecordingState.Paused:
                // 暂停只是不写文件，保持同一 recorder。
                break;
        }

        State = state;
    }

    private void StartRecorder()
    {
        _recorder = new VboRecorder(
            Path.Combine(
                AppContext.BaseDirectory,
                "Recordings",
                $"CMTS_{DateTime.Now:yyyyMMdd_HHmmss}.vbo"),
            SessionMetadata.Current.Clone());
    }

    /// <summary>
    /// 仅在 <see cref="RecordingState.Recording"/> 时写入；
    /// 首样本锁定 <see cref="RecordingStartTimestamp"/>。
    /// TryWrite 失败会计入 <see cref="DroppedSamples"/>。
    /// </summary>
    public void TryWriteIfRecording(VehicleSample sample)
    {
        if (_disposed || State != RecordingState.Recording)
            return;

        RecordingStartTimestamp ??= sample.Timestamp;
        _recorder?.TryWrite(sample);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _recorder?.Dispose();
        _recorder = null;
        State = RecordingState.Stopped;
        RecordingStartTimestamp = null;
    }
}
