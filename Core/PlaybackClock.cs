namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// Offline replay clock: advances along <see cref="VehicleSample.Timestamp"/>
/// (Unix milliseconds) at wall-clock rate times <see cref="Speed"/>.
/// Speed 1.0 is real time.
/// </summary>
public sealed class PlaybackClock
{
    public static IReadOnlyList<double> StandardSpeeds { get; } =
        new[] { 0.25, 0.5, 1.0, 2.0, 4.0, 8.0 };

    private IReadOnlyList<VehicleSample> _samples = Array.Empty<VehicleSample>();
    private long _startMs;
    private long _endMs;
    private long _currentMs;
    private int _index = -1;
    private bool _playing;
    private double _speed = 1.0;
    private long _anchorWallMs;
    private long _anchorTsMs;

    public bool IsPlaying => _playing;
    public double Speed => _speed;
    public int SampleCount => _samples.Count;
    public bool CanPlay => _samples.Count >= 2 && DurationMs > 0;
    public long StartTimestampMs => _startMs;
    public long EndTimestampMs => _endMs;
    public long DurationMs => Math.Max(0L, _endMs - _startMs);
    public long CurrentTimestampMs => _currentMs;
    public int CurrentIndex => _index;
    public VehicleSample? CurrentSample =>
        _index >= 0 && _index < _samples.Count ? _samples[_index] : null;

    public double Progress =>
        DurationMs <= 0
            ? 0
            : Math.Clamp((_currentMs - _startMs) / (double)DurationMs, 0, 1);

    public void SetSamples(IReadOnlyList<VehicleSample>? samples, bool resetPosition = true)
    {
        _samples = samples ?? Array.Empty<VehicleSample>();
        if (_samples.Count == 0)
        {
            _startMs = 0;
            _endMs = 0;
            _currentMs = 0;
            _index = -1;
            _playing = false;
            return;
        }

        _startMs = _samples[0].Timestamp;
        _endMs = _samples[^1].Timestamp;
        if (_endMs < _startMs)
            _endMs = _startMs;

        if (!CanPlay)
            _playing = false;

        if (resetPosition || _currentMs < _startMs || _currentMs > _endMs)
            SeekToTimestamp(_startMs, wallMs: 0);
        else
            SeekToTimestamp(_currentMs, wallMs: 0);
    }

    public void Play(long wallMs)
    {
        if (!CanPlay)
            return;

        if (_currentMs >= _endMs)
            SeekToTimestamp(_startMs, wallMs);

        _playing = true;
        _anchorWallMs = wallMs;
        _anchorTsMs = _currentMs;
    }

    public void Pause() => _playing = false;

    public void Stop()
    {
        _playing = false;
        if (_samples.Count == 0)
        {
            _currentMs = 0;
            _index = -1;
            return;
        }

        SeekToTimestamp(_startMs, wallMs: 0);
    }

    public void SetSpeed(double speed, long wallMs)
    {
        if (speed <= 0 || !double.IsFinite(speed))
            return;

        _speed = speed;
        if (_playing)
        {
            _anchorWallMs = wallMs;
            _anchorTsMs = _currentMs;
        }
    }

    public void SeekToTimestamp(long timestampMs, long wallMs)
    {
        if (_samples.Count == 0)
        {
            _currentMs = 0;
            _index = -1;
            return;
        }

        _currentMs = Math.Clamp(timestampMs, _startMs, _endMs);
        _index = FindIndexAtOrBefore(_currentMs);
        if (_playing)
        {
            _anchorWallMs = wallMs;
            _anchorTsMs = _currentMs;
        }
    }

    public void SeekToProgress(double progress, long wallMs)
    {
        var p = Math.Clamp(progress, 0, 1);
        SeekToTimestamp(_startMs + (long)Math.Round(p * DurationMs), wallMs);
    }

    /// <summary>
    /// Advance from the play anchor. Returns true if the sample index or
    /// playing state changed.
    /// </summary>
    public bool Tick(long wallMs)
    {
        if (!_playing || !CanPlay)
            return false;

        var elapsed = wallMs - _anchorWallMs;
        if (elapsed < 0)
            elapsed = 0;

        var target = _anchorTsMs + (long)Math.Round(elapsed * _speed);
        var ended = false;
        if (target >= _endMs)
        {
            target = _endMs;
            ended = true;
        }

        var prevIndex = _index;
        var wasPlaying = _playing;
        _currentMs = target;
        _index = FindIndexAtOrBefore(_currentMs);
        if (ended)
            _playing = false;

        return _index != prevIndex || _playing != wasPlaying;
    }

    public string FormatPositionLabel() =>
        $"{FormatElapsed(_currentMs - _startMs)} / {FormatElapsed(DurationMs)}";

    public static string FormatElapsed(long elapsedMs)
    {
        if (elapsedMs < 0)
            elapsedMs = 0;

        var ts = TimeSpan.FromMilliseconds(elapsedMs);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 100}";

        return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}.{ts.Milliseconds / 100}";
    }

    private int FindIndexAtOrBefore(long timestampMs)
    {
        var samples = _samples;
        if (samples.Count == 0)
            return -1;

        var lo = 0;
        var hi = samples.Count - 1;
        if (timestampMs <= samples[0].Timestamp)
            return 0;
        if (timestampMs >= samples[hi].Timestamp)
            return hi;

        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            var midTs = samples[mid].Timestamp;
            if (midTs == timestampMs)
                return mid;
            if (midTs < timestampMs)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        return Math.Max(0, hi);
    }
}
