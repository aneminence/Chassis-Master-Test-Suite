using System.IO;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Recorder;

namespace Chassis_Master_Test_Suite.Session;

/// <summary>
/// Local multi-device VBO writers used by Remote Rec● / Rec■.
/// Independent of the single-stream <see cref="RecordingSession"/> (right Start/Stop).
/// One file per vehicle/device id; mid-session add opens a new file, remove finalizes only that file.
/// </summary>
public sealed class MultiDeviceRecordingSession : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly Dictionary<string, DeviceRecorder> _recorders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _closedThisSession = new(StringComparer.Ordinal);
    private bool _recording;
    private bool _disposed;

    public MultiDeviceRecordingSession()
        : this(Path.Combine(AppContext.BaseDirectory, "Recordings")) { }

    internal MultiDeviceRecordingSession(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory is required.", nameof(directory));
        _directory = directory;
    }

    public bool IsRecording
    {
        get { lock (_gate) return _recording; }
    }

    public IReadOnlyList<string> CurrentFilePaths
    {
        get
        {
            lock (_gate)
                return CurrentPathsUnlocked();
        }
    }

    public IReadOnlyList<string> Start(
        IEnumerable<(string DeviceId, string? Label)> devices,
        DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var wasRecording = _recording;
            _recording = true;
            if (!wasRecording)
                _closedThisSession.Clear();
            var stamp = now ?? DateTime.Now;
            foreach (var device in devices)
            {
                if (!string.IsNullOrWhiteSpace(device.DeviceId))
                    _closedThisSession.Remove(device.DeviceId.Trim());
                EnsureUnlocked(device.DeviceId, device.Label, stamp);
            }
            return CurrentPathsUnlocked();
        }
    }

    public string? EnsureDevice(string deviceId, string? label = null, DateTime? now = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_recording)
                return null;
            if (!string.IsNullOrWhiteSpace(deviceId))
                _closedThisSession.Remove(deviceId.Trim());
            return EnsureUnlocked(deviceId, label, now ?? DateTime.Now);
        }
    }

    public void TryWrite(VehicleSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        lock (_gate)
        {
            if (_disposed || !_recording)
                return;

            var key = LiveVehicleRoster.ResolveKey(sample);
            if (!_recorders.TryGetValue(key, out var recorder))
            {
                if (_closedThisSession.Contains(key))
                    return;
                EnsureUnlocked(key, sample.VehicleLabel, DateTime.Now);
                if (!_recorders.TryGetValue(key, out recorder))
                    return;
            }

            recorder.Recorder.TryWrite(sample);
        }
    }

    public Task<string?> StopDeviceAsync(string deviceId)
    {
        Task? stopping = null;
        string? path = null;
        lock (_gate)
        {
            var key = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
            if (key is not null)
                _closedThisSession.Add(key);
            if (key is not null && _recorders.Remove(key, out var recorder))
            {
                path = recorder.FilePath;
                stopping = recorder.Recorder.StopAsync();
            }
        }

        if (stopping is null)
            return Task.FromResult<string?>(null);

        return WaitPathAsync(stopping, path);
    }

    public Task<IReadOnlyList<string>> StopAsync()
    {
        List<(Task Task, string Path)> stopping;
        lock (_gate)
        {
            _recording = false;
            _closedThisSession.Clear();
            stopping = _recorders.Values
                .Select(recorder => (recorder.Recorder.StopAsync(), recorder.FilePath))
                .ToList();
            _recorders.Clear();
        }

        return WaitAllAsync(stopping);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_gate)
            _disposed = true;
        return new ValueTask(StopAsync());
    }

    internal static string BuildFileStem(DateTime now, string deviceId, string? label)
    {
        var idToken = SanitizeFileToken(deviceId);
        if (idToken.Length == 0)
            idToken = "device";
        var labelToken = SanitizeFileToken(label);
        if (labelToken.Length == 0 ||
            string.Equals(labelToken, idToken, StringComparison.OrdinalIgnoreCase))
            return $"CMTS_{now:yyyyMMdd_HHmmss}_{idToken}";
        return $"CMTS_{now:yyyyMMdd_HHmmss}_{labelToken}_{idToken}";
    }

    internal static string SanitizeFileToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        // Use a portable set so stems stay valid on Windows even if tests run on Linux CI.
        var invalid = Path.GetInvalidFileNameChars()
            .Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*'])
            .Distinct()
            .ToArray();
        var chars = value.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c <= 32 || Array.IndexOf(invalid, c) >= 0)
                chars[i] = '_';
        }

        var text = new string(chars);
        while (text.Contains("__", StringComparison.Ordinal))
            text = text.Replace("__", "_", StringComparison.Ordinal);
        text = text.Trim('_');
        const int maxLength = 40;
        if (text.Length > maxLength)
            text = text[..maxLength].Trim('_');
        return text;
    }

    private string? EnsureUnlocked(string deviceId, string? label, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;
        var key = deviceId.Trim();
        if (_recorders.TryGetValue(key, out var existing))
            return existing.FilePath;

        var stem = BuildFileStem(now, key, label);
        var recorder = RecordingSession.CreateNamedRecorder(_directory, stem);
        _recorders[key] = new DeviceRecorder(recorder, recorder.FilePath);
        return recorder.FilePath;
    }

    private List<string> CurrentPathsUnlocked() =>
        _recorders.Values.Select(recorder => recorder.FilePath).ToList();

    private static async Task<string?> WaitPathAsync(Task stopping, string? path)
    {
        await stopping.ConfigureAwait(false);
        return path;
    }

    private static async Task<IReadOnlyList<string>> WaitAllAsync(
        List<(Task Task, string Path)> stopping)
    {
        if (stopping.Count == 0)
            return Array.Empty<string>();

        var tasks = stopping.Select(item => item.Task).ToArray();
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            var first = tasks.Select(task => task.Exception).FirstOrDefault(ex => ex is not null);
            if (first is not null)
                throw first.InnerException ?? first;
            throw;
        }

        return stopping.Select(item => item.Path).ToList();
    }

    private readonly record struct DeviceRecorder(VboRecorder Recorder, string FilePath);
}
