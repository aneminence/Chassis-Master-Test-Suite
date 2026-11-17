using System.Runtime.CompilerServices;

namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// Resolves map body yaw (degrees clockwise from north) without changing recorded channels.
/// Offline sample lists are immutable or append-only. Held ranges are weakly keyed by list,
/// so independent files / compare cursors / backwards seeks cannot inherit each other's yaw.
/// </summary>
public sealed class VehicleHeadingResolver
{
    // GPS course is unreliable near rest. This is km/h, not m/s.
    public const double MinimumCourseSpeedKph = 1.0;
    // Import-only provenance. Existing typed/core values stay backward compatible.
    public const string MissingSpeedKey = "cmts_internal_missing_speed";
    public const string MissingHeadingKey = "cmts_internal_missing_heading";

    public static bool IsInternalChannel(string id) =>
        string.Equals(id, MissingSpeedKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, MissingHeadingKey, StringComparison.OrdinalIgnoreCase);
    private const int MaxHeldRanges = 64;
    private readonly ConditionalWeakTable<IReadOnlyList<VehicleSample>, HeldRanges> _held = new();

    public double Resolve(VehicleSample sample, IReadOnlyList<VehicleSample>? samples = null, int index = -1)
    {
        if (TryBodyHeading(sample, out var heading))
            return heading;

        if (samples is null || index < 0 || index >= samples.Count || !ReferenceEquals(samples[index], sample))
            return TryCourseHeading(sample, out heading) ? heading : 0;

        if (TryMovingHeading(sample, samples, index, out heading))
            return heading;

        var ranges = _held.GetValue(samples, static _ => new HeldRanges());
        ranges.Validate(samples);
        if (ranges.TryFind(index, out var held))
            return held.Heading;

        // First seek into an unseen stop scans its preceding history once. Subsequent
        // playback extends a sparse range, with no per-sample heading array or repeated scan.
        var start = index;
        for (var previous = index - 1; previous >= 0; previous--)
        {
            if (ranges.TryFind(previous, out held))
            {
                start = held.Start;
                heading = held.Heading;
                ranges.Add(start, index, heading);
                return heading;
            }

            var candidate = samples[previous];
            if (TryBodyHeading(candidate, out heading)
                || TryMovingHeading(candidate, samples, previous, out heading))
            {
                ranges.Add(start, index, heading);
                return heading;
            }
            start = previous;
        }

        // A file beginning at rest has no preceding travel direction. Keep the first
        // recorded course (or north when absent), rather than following GPS drift.
        heading = TryCourseHeading(samples[0], out var initial) ? initial : 0;
        ranges.Add(0, index, heading);
        return heading;
    }

    private static bool TryBodyHeading(VehicleSample sample, out double heading)
    {
        var channels = sample.Channels;
        // XcomImporter only writes a finite heading when NcomDecoder considers
        // navigation valid. Read Channels, since typed Heading converts NaN to zero.
        if (channels.ContainsKey("nav_status") && channels.ContainsKey("pitch_deg")
            && channels.ContainsKey("roll_deg") && TryHeading(channels, ChannelIds.Heading, out heading))
            return true;

        // Documented Racelogic body-heading channels. Robot_Head / Slip_Head are
        // deliberately excluded: their source can change with lock / integration status.
        // No undocumented DualStatus / Kalman status bit interpretation is invented here.
        return TryHeading(channels, "Head_imu2", out heading)
            || TryHeading(channels, "Head_imu", out heading)
            || TryHeading(channels, "True_Head2", out heading)
            || TryHeading(channels, "True_Head", out heading);
    }

    private static bool TryMovingHeading(VehicleSample sample, IReadOnlyList<VehicleSample> samples,
        int index, out double heading)
    {
        heading = 0;
        if (sample.Channels.ContainsKey(MissingSpeedKey))
            return TryTrackBearing(samples, index, out heading, inferMotion: true);
        var speed = sample.Channels.TryGetValue(ChannelIds.Velocity, out var measured)
            ? measured : sample.SpeedKph;
        if (!double.IsFinite(speed) || Math.Abs(speed) < MinimumCourseSpeedKph)
            return false;
        return TryCourseHeading(sample, out heading) || TryTrackBearing(samples, index, out heading);
    }

    private static bool TryCourseHeading(VehicleSample sample, out double heading)
    {
        // Do not turn an explicitly invalid channel (NaN) into the typed zero default.
        heading = 0;
        if (sample.Channels.ContainsKey(MissingHeadingKey))
            return false;
        if (sample.Channels.TryGetValue(ChannelIds.Heading, out var channel))
            return Normalize(channel, out heading);
        // Typed zero alone cannot distinguish north from a missing/default value.
        return sample.Heading != 0 && Normalize(sample.Heading, out heading);
    }

    private static bool TryHeading(IReadOnlyDictionary<string, double> channels, string name, out double heading)
    {
        heading = 0;
        return channels.TryGetValue(name, out var value) && Normalize(value, out heading);
    }

    private static bool Normalize(double value, out double heading)
    {
        heading = 0;
        if (!double.IsFinite(value) || value < -360 || value > 360)
            return false;
        heading = ((value % 360) + 360) % 360;
        return true;
    }

    private static bool TryTrackBearing(IReadOnlyList<VehicleSample> samples, int index, out double heading,
        bool inferMotion = false)
    {
        // At most ten probes in each direction; never walk a full track on every tick.
        // A 0.5 m baseline avoids turning sub-centimetre coordinate noise into yaw.
        for (var offset = 1; offset <= 512; offset *= 2)
        {
            if (index >= offset && TryBearing(samples[index - offset], samples[index], out heading, inferMotion,
                allowLongInterval: offset == 1))
                return true;
        }
        // Missing speed: only the first point may infer motion from the future.
        // A stopped cursor must not turn in anticipation of a later departure.
        if (inferMotion && index > 0)
        {
            heading = 0;
            return false;
        }
        for (var offset = 1; offset <= 512; offset *= 2)
        {
            if (index + offset < samples.Count && TryBearing(samples[index], samples[index + offset], out heading, inferMotion,
                allowLongInterval: offset == 1))
                return true;
        }
        heading = 0;
        return false;
    }

    private static bool TryBearing(VehicleSample a, VehicleSample b, out double heading, bool inferMotion,
        bool allowLongInterval)
    {
        heading = 0;
        if (!ValidGps(a) || !ValidGps(b))
            return false;
        var dLon = b.Longitude - a.Longitude;
        if (dLon > 180) dLon -= 360;
        if (dLon < -180) dLon += 360;
        var dx = dLon * 111320 * Math.Cos((a.Latitude + b.Latitude) * Math.PI / 360);
        var dy = (b.Latitude - a.Latitude) * 111320;
        if (dx * dx + dy * dy < 0.25)
            return false;
        if (inferMotion)
        {
            var seconds = (b.Timestamp - (double)a.Timestamp) / 1000;
            // Sparse KML may have seconds between adjacent fixes. Only wider probes
            // need the 2 s bound, to avoid borrowing motion from before a long stop.
            if (seconds <= 0 || (!allowLongInterval && seconds > 2)
                || Math.Sqrt(dx * dx + dy * dy) / seconds * 3.6 < MinimumCourseSpeedKph)
                return false;
        }
        heading = (Math.Atan2(dx, dy) * 180 / Math.PI + 360) % 360;
        return true;
    }

    private static bool ValidGps(VehicleSample sample) =>
        double.IsFinite(sample.Latitude) && double.IsFinite(sample.Longitude)
        && Math.Abs(sample.Latitude) <= 90 && Math.Abs(sample.Longitude) <= 180
        && (Math.Abs(sample.Latitude) >= 1e-5 || Math.Abs(sample.Longitude) >= 1e-5);

    private readonly record struct HeldRange(int Start, int End, double Heading);

    private sealed class HeldRanges
    {
        private readonly List<HeldRange> _ranges = new(MaxHeldRanges);
        private VehicleSample? _first;
        private VehicleSample? _last;
        private int _count;

        public void Validate(IReadOnlyList<VehicleSample> samples)
        {
            if (_count > samples.Count || !ReferenceEquals(_first, samples[0])
                || (_count > 0 && !ReferenceEquals(_last, samples[_count - 1])))
                _ranges.Clear();
            _count = samples.Count;
            _first = samples[0];
            _last = samples[^1];
        }

        public bool TryFind(int index, out HeldRange found)
        {
            for (var i = _ranges.Count - 1; i >= 0; i--)
            {
                var range = _ranges[i];
                if (index >= range.Start && index <= range.End)
                {
                    found = range;
                    return true;
                }
            }
            found = default;
            return false;
        }

        public void Add(int start, int end, double heading)
        {
            for (var i = _ranges.Count - 1; i >= 0; i--)
            {
                var range = _ranges[i];
                if (range.Heading == heading && range.End + 1 >= start && range.Start <= end + 1)
                {
                    start = Math.Min(start, range.Start);
                    end = Math.Max(end, range.End);
                    _ranges.RemoveAt(i);
                }
            }
            if (_ranges.Count == MaxHeldRanges)
                _ranges.RemoveAt(0);
            _ranges.Add(new HeldRange(start, end, heading));
        }
    }
}
