namespace Chassis_Master_Test_Suite.Core;

public readonly record struct TrackUpdateStamp(
    object Source, object? First, object? Last, int Count,
    string Name, string Color, bool IsLive);

/// <summary>Throttle scene geometry, independently of snapshot array identity.</summary>
public sealed class TrackUpdateGate
{
    private TrackUpdateStamp[] _sent = Array.Empty<TrackUpdateStamp>();
    private long _lastSentAt;

    public bool TryAccept(IReadOnlyList<TrackUpdateStamp> current, long nowMilliseconds)
    {
        var sameSources = current.Count == _sent.Length && current.Count > 0;
        var unchanged = sameSources;
        var live = false;
        for (var i = 0; i < current.Count; i++)
        {
            live |= current[i].IsLive;
            if (i >= _sent.Length) continue;
            var a = current[i];
            var b = _sent[i];
            sameSources &= Equals(a.Source, b.Source) && a.Name == b.Name
                && a.Color == b.Color && a.IsLive == b.IsLive;
            unchanged &= a.Equals(b);
        }
        if (unchanged || (sameSources && live && nowMilliseconds - _lastSentAt < 1_000))
            return false;
        _sent = current.ToArray();
        _lastSentAt = nowMilliseconds;
        return true;
    }

    public void Reset() => _sent = Array.Empty<TrackUpdateStamp>();
}
