using System.IO;
using System.Globalization;
using System.Text.Json;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Communication.Remote;

/// <summary>
/// CMTS remote protocol v0.1 JSON helpers: envelope + sample → VehicleSample.
/// Does not touch GSpot parsers.
/// </summary>
public static class CMTSRemoteParser
{
    public const int ProtocolVersion = 1;

    public sealed class Envelope
    {
        public int V { get; init; }
        public string Type { get; init; } = "";
        public string? Id { get; init; }
        public long Ts { get; init; }
        public string? DeviceId { get; init; }
        public JsonElement Payload { get; init; }
    }

    public sealed class SampleParseResult
    {
        public VehicleSample? Sample { get; init; }
        public ulong Seq { get; init; }
        public long? ReceiveTimeMs { get; init; }
        public string? SkipReason { get; init; }
    }

    public sealed class HelloAck
    {
        public bool Ok { get; init; }
        public string? ErrorCode { get; init; }
        public string? DeviceId { get; init; }
        public string? DeviceLabel { get; init; }
        public string? RecordState { get; init; }
        public int Proto { get; init; }
    }

    public sealed class RecordStatus
    {
        public string State { get; init; } = "IDLE";
        public string? FileId { get; init; }
        public long? StartedAtMs { get; init; }
        public string? Reason { get; init; }
        public bool Ok { get; init; } = true;
    }

    /// <summary>
    /// device.status payload (v0.1). Satellites live here today; IMU extras stay on stream.sample.
    /// </summary>
    public sealed class DeviceStatus
    {
        public string? RecordState { get; init; }
        public bool? GnssFix { get; init; }
        public double? SatellitesVisible { get; init; }
        public double? SatellitesUsed { get; init; }
        public bool? ServiceAlive { get; init; }
        public bool? Running { get; init; }
        public double? StorageFreeMb { get; init; }

        /// <summary>Android-aligned channel ids present in this status.</summary>
        public IReadOnlyDictionary<string, double> Channels { get; init; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Meta / typed-core JSON keys that must not be pass-through under their raw names
    /// (typed fields already land under <see cref="ChannelIds"/>).
    /// </summary>
    private static readonly HashSet<string> ReservedPayloadKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "seq", "measureTimeMs", "receiveTimeMs", "vehicleLabel", "valid", "deviceId",
        "speedKph", "ax", "ay", "az", "yawRateDps", "lat", "lon", "altM", "headingDeg"
    };

    /// <summary>
    /// Android Live/CSV source → component suffixes when payload carries an array/object.
    /// Only used to name Channels; never invents values that are not on the wire.
    /// </summary>
    private static readonly Dictionary<string, string[]> AndroidVectorComponents =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["phone.accel"] = ["x", "y", "z"],
            ["vehicle.accel"] = ["x", "y", "z"],
            ["phone.gyro"] = ["x", "y", "z"],
            ["vehicle.gyro"] = ["x", "y", "z"],
            ["vehicle.attitude"] = ["roll", "pitch", "yaw"],
            ["phone.attitude"] = ["pitch", "roll", "azimuth", "headingAcc"],
            ["phone.rotation_vector"] = ["x", "y", "z", "w", "headingAcc"],
            ["phone.gps"] = ["lat", "lon", "alt", "speed", "bearing", "horizAcc", "vertAcc", "speedAcc"],
            ["phone.satellites"] = ["visible", "used"],
        };

    public const string ChannelSatellitesVisible = "phone.satellites.visible";
    public const string ChannelSatellitesUsed = "phone.satellites.used";

    public static bool TryParseEnvelope(string json, out Envelope? envelope)
    {
        envelope = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return TryParseEnvelope(doc.RootElement, out envelope);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseEnvelope(JsonElement root, out Envelope? envelope)
    {
        envelope = null;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (!root.TryGetProperty("type", out var typeEl) ||
            typeEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var type = typeEl.GetString();
        if (string.IsNullOrWhiteSpace(type))
            return false;

        var v = 1;
        if (root.TryGetProperty("v", out var vEl) &&
            vEl.ValueKind == JsonValueKind.Number &&
            vEl.TryGetInt32(out var vv))
        {
            v = vv;
        }

        string? id = null;
        if (root.TryGetProperty("id", out var idEl) &&
            idEl.ValueKind == JsonValueKind.String)
        {
            id = idEl.GetString();
        }

        long ts = 0;
        if (root.TryGetProperty("ts", out var tsEl))
            TryReadInt64(tsEl, out ts);

        string? deviceId = null;
        if (root.TryGetProperty("deviceId", out var didEl) &&
            didEl.ValueKind == JsonValueKind.String)
        {
            deviceId = didEl.GetString();
        }

        JsonElement payload = default;
        var hasPayload = root.TryGetProperty("payload", out payload);

        envelope = new Envelope
        {
            V = v,
            Type = type!,
            Id = id,
            Ts = ts,
            DeviceId = deviceId,
            Payload = hasPayload ? payload.Clone() : default
        };
        return true;
    }

    public static HelloAck ParseHelloAck(Envelope env)
    {
        var payload = env.Payload;
        var ok = false;
        string? errorCode = null;
        string? label = null;
        string? recordState = null;
        var proto = 0;

        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("ok", out var okEl))
            {
                ok = okEl.ValueKind == JsonValueKind.True ||
                     (okEl.ValueKind == JsonValueKind.String &&
                      string.Equals(okEl.GetString(), "true", StringComparison.OrdinalIgnoreCase));
            }

            if (payload.TryGetProperty("errorCode", out var errEl) &&
                errEl.ValueKind == JsonValueKind.String)
            {
                errorCode = errEl.GetString();
            }

            if (payload.TryGetProperty("deviceLabel", out var lbl) &&
                lbl.ValueKind == JsonValueKind.String)
            {
                label = lbl.GetString();
            }

            if (payload.TryGetProperty("recordState", out var rs) &&
                rs.ValueKind == JsonValueKind.String)
            {
                recordState = rs.GetString();
            }

            if (payload.TryGetProperty("proto", out var p) &&
                p.ValueKind == JsonValueKind.Number &&
                p.TryGetInt32(out var pi))
            {
                proto = pi;
            }
        }

        if (!ok && string.IsNullOrWhiteSpace(errorCode))
            errorCode = "auth_failed";

        return new HelloAck
        {
            Ok = ok,
            ErrorCode = errorCode,
            DeviceId = env.DeviceId,
            DeviceLabel = label,
            RecordState = recordState,
            Proto = proto
        };
    }

    public static RecordStatus ParseRecordStatus(JsonElement payload)
    {
        var state = "IDLE";
        string? fileId = null;
        long? started = null;
        string? reason = null;
        var ok = true;

        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("state", out var st) &&
                st.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(st.GetString()))
            {
                state = st.GetString()!;
            }

            if (payload.TryGetProperty("fileId", out var fid) &&
                fid.ValueKind == JsonValueKind.String)
            {
                fileId = fid.GetString();
            }

            if (payload.TryGetProperty("startedAtMs", out var sam) &&
                TryReadInt64(sam, out var s))
            {
                started = s;
            }

            if (payload.TryGetProperty("reason", out var r) &&
                r.ValueKind == JsonValueKind.String)
            {
                reason = r.GetString();
            }

            if (payload.TryGetProperty("ok", out var okEl))
            {
                if (okEl.ValueKind == JsonValueKind.False)
                    ok = false;
                else if (okEl.ValueKind == JsonValueKind.True)
                    ok = true;
            }
        }

        return new RecordStatus
        {
            State = state,
            FileId = fileId,
            StartedAtMs = started,
            Reason = reason,
            Ok = ok
        };
    }

    /// <summary>
    /// Map stream.sample payload to VehicleSample.
    /// Missing measureTimeMs → skip (never forge with receive/UtcNow).
    /// Invalid / null fields → NaN typed + omitted from Channels (never silent 0).
    /// </summary>
    public static SampleParseResult TryMapSample(
        Envelope env,
        string? fallbackDeviceId = null)
    {
        var payload = env.Payload;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return new SampleParseResult { SkipReason = "payload_not_object" };
        }

        if (!TryReadUInt64(payload, "seq", out var seq))
        {
            return new SampleParseResult { SkipReason = "missing_seq" };
        }

        long? measureTimeMs = null;
        if (payload.TryGetProperty("measureTimeMs", out var mt) &&
            mt.ValueKind != JsonValueKind.Null &&
            TryReadInt64(mt, out var mtv))
        {
            measureTimeMs = mtv;
        }

        if (measureTimeMs is null)
        {
            return new SampleParseResult
            {
                Seq = seq,
                SkipReason = "missing_measureTimeMs"
            };
        }

        long? receiveTimeMs = null;
        if (payload.TryGetProperty("receiveTimeMs", out var rt) &&
            rt.ValueKind != JsonValueKind.Null &&
            TryReadInt64(rt, out var rtv))
        {
            receiveTimeMs = rtv;
        }

        var valid = ReadValidMap(payload);

        var speed = ReadOptionalDouble(payload, "speedKph", valid, "speed");
        var ax = ReadOptionalDouble(payload, "ax", valid, "imu");
        var ay = ReadOptionalDouble(payload, "ay", valid, "imu");
        var az = ReadOptionalDouble(payload, "az", valid, "imu");
        var yaw = ReadOptionalDouble(payload, "yawRateDps", valid, "imu");
        var lat = ReadOptionalDouble(payload, "lat", valid, "gnss");
        var lon = ReadOptionalDouble(payload, "lon", valid, "gnss");
        var alt = ReadOptionalDouble(payload, "altM", valid, "gnss");
        var heading = ReadOptionalDouble(payload, "headingDeg", valid, "heading");

        string? label = null;
        if (payload.TryGetProperty("vehicleLabel", out var vl) &&
            vl.ValueKind == JsonValueKind.String)
        {
            label = vl.GetString();
        }

        var deviceId = env.DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
            deviceId = fallbackDeviceId;

        var channels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        AddChannel(channels, ChannelIds.Velocity, speed);
        AddChannel(channels, ChannelIds.Longacc, ax);
        AddChannel(channels, ChannelIds.Latacc, ay);
        AddChannel(channels, ChannelIds.ZAccel, az);
        AddChannel(channels, ChannelIds.YawRate, yaw);
        AddChannel(channels, ChannelIds.Latitude, lat);
        AddChannel(channels, ChannelIds.Longitude, lon);
        AddChannel(channels, ChannelIds.Height, alt);
        AddChannel(channels, ChannelIds.Heading, heading);

        // Pass-through numeric extras (Android phone.*/vehicle.* when present on wire).
        CollectExtraChannels(payload, channels);

        var sample = new VehicleSample
        {
            Timestamp = measureTimeMs.Value,
            Sequence = unchecked((long)seq),
            SpeedKph = speed ?? double.NaN,
            LongitudinalAcceleration = ax ?? double.NaN,
            LateralAcceleration = ay ?? double.NaN,
            VerticalAcceleration = az ?? double.NaN,
            YawRate = yaw ?? double.NaN,
            Latitude = lat ?? double.NaN,
            Longitude = lon ?? double.NaN,
            Altitude = alt ?? double.NaN,
            Heading = heading ?? double.NaN,
            VehicleId = deviceId,
            VehicleLabel = label ?? deviceId,
            Channels = channels
        };

        return new SampleParseResult
        {
            Sample = sample,
            Seq = seq,
            ReceiveTimeMs = receiveTimeMs
        };
    }

    public static string BuildEnvelopeJson(
        string type,
        string id,
        object? payload,
        string? deviceId = null,
        long? tsMs = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", ProtocolVersion);
            writer.WriteString("type", type);
            writer.WriteString("id", id);
            writer.WriteNumber("ts", tsMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (!string.IsNullOrWhiteSpace(deviceId))
                writer.WriteString("deviceId", deviceId);
            writer.WritePropertyName("payload");
            if (payload is null)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else if (payload is JsonElement je)
            {
                je.WriteTo(writer);
            }
            else
            {
                JsonSerializer.Serialize(writer, payload);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static DeviceStatus ParseDeviceStatus(JsonElement payload)
    {
        string? recordState = null;
        bool? gnssFix = null;
        double? satVis = null;
        double? satUsed = null;
        bool? serviceAlive = null;
        bool? running = null;
        double? storageFreeMb = null;
        var channels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("recordState", out var rs) &&
                rs.ValueKind == JsonValueKind.String)
            {
                recordState = rs.GetString();
            }

            gnssFix = ReadOptionalBool(payload, "gnssFix");
            serviceAlive = ReadOptionalBool(payload, "serviceAlive");
            running = ReadOptionalBool(payload, "running");
            satVis = ReadOptionalDoubleUngated(payload, "satellitesVisible");
            satUsed = ReadOptionalDoubleUngated(payload, "satellitesUsed");
            storageFreeMb = ReadOptionalDoubleUngated(payload, "storageFreeMb");

            // Align status sats with Android Sample key phone.satellites.{visible,used}.
            AddChannel(channels, ChannelSatellitesVisible, satVis);
            AddChannel(channels, ChannelSatellitesUsed, satUsed);
            // Catalog short name used by VBO seed ("sats") ← used count when present.
            AddChannel(channels, "sats", satUsed);
        }

        return new DeviceStatus
        {
            RecordState = recordState,
            GnssFix = gnssFix,
            SatellitesVisible = satVis,
            SatellitesUsed = satUsed,
            ServiceAlive = serviceAlive,
            Running = running,
            StorageFreeMb = storageFreeMb,
            Channels = channels
        };
    }

    /// <summary>
    /// Merge status-derived channels into a sample Channels dict (slow fields ride on live samples).
    /// </summary>
    public static void MergeStatusChannels(
        Dictionary<string, double> channels,
        DeviceStatus? status)
    {
        if (status is null || channels is null)
            return;

        foreach (var kv in status.Channels)
            channels[kv.Key] = kv.Value;
    }

    /// <summary>
    /// Walk payload for numeric extras not covered by typed core mapping.
    /// Supports flat numbers, arrays (Android vector sources), and shallow objects.
    /// </summary>
    private static void CollectExtraChannels(
        JsonElement payload,
        Dictionary<string, double> channels)
    {
        foreach (var prop in payload.EnumerateObject())
        {
            if (ReservedPayloadKeys.Contains(prop.Name))
                continue;

            FlattenJsonValue(prop.Name, prop.Value, channels);
        }
    }

    private static void FlattenJsonValue(
        string key,
        JsonElement el,
        Dictionary<string, double> channels)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d) && double.IsFinite(d))
        {
            channels[key] = d;
            return;
        }

        if (el.ValueKind == JsonValueKind.String &&
            double.TryParse(
                el.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out d) &&
            double.IsFinite(d))
        {
            channels[key] = d;
            return;
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            string[]? names = null;
            AndroidVectorComponents.TryGetValue(key, out names);
            var index = 0;
            foreach (var item in el.EnumerateArray())
            {
                string childKey;
                if (names is not null && index < names.Length)
                    childKey = key + "." + names[index];
                else
                    childKey = key + "." + index.ToString(CultureInfo.InvariantCulture);

                if (item.ValueKind == JsonValueKind.Number &&
                    item.TryGetDouble(out var v) &&
                    double.IsFinite(v))
                {
                    channels[childKey] = v;
                }
                else if (item.ValueKind == JsonValueKind.String &&
                         double.TryParse(
                             item.GetString(),
                             NumberStyles.Float,
                             CultureInfo.InvariantCulture,
                             out v) &&
                         double.IsFinite(v))
                {
                    channels[childKey] = v;
                }

                index++;
            }

            return;
        }

        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var child in el.EnumerateObject())
            {
                var childKey = key + "." + child.Name;
                // Prefer Android component names when parent is a known vector and
                // child is a numeric index-like name — still accept arbitrary child names.
                FlattenJsonValue(childKey, child.Value, channels);
            }
        }
    }

    private static bool? ReadOptionalBool(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var el) ||
            el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.True)
            return true;
        if (el.ValueKind == JsonValueKind.False)
            return false;
        return null;
    }

    private static double? ReadOptionalDoubleUngated(JsonElement payload, string field)
    {
        if (!payload.TryGetProperty(field, out var el) ||
            el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d) && double.IsFinite(d))
            return d;

        if (el.ValueKind == JsonValueKind.String &&
            double.TryParse(
                el.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out d) &&
            double.IsFinite(d))
        {
            return d;
        }

        return null;
    }

    private static Dictionary<string, bool> ReadValidMap(JsonElement payload)
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (!payload.TryGetProperty("valid", out var valid) ||
            valid.ValueKind != JsonValueKind.Object)
        {
            return map;
        }

        foreach (var prop in valid.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.True)
                map[prop.Name] = true;
            else if (prop.Value.ValueKind == JsonValueKind.False)
                map[prop.Name] = false;
        }

        return map;
    }

    /// <summary>
    /// Returns null when missing, JSON null, or valid[flag]=false.
    /// Never returns 0 for those cases.
    /// </summary>
    private static double? ReadOptionalDouble(
        JsonElement payload,
        string field,
        IReadOnlyDictionary<string, bool> valid,
        string? validKey)
    {
        if (validKey is not null &&
            valid.TryGetValue(validKey, out var flag) &&
            !flag)
        {
            return null;
        }

        if (!payload.TryGetProperty(field, out var el) ||
            el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d))
            return d;

        if (el.ValueKind == JsonValueKind.String &&
            double.TryParse(
                el.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out d))
        {
            return d;
        }

        return null;
    }

    private static void AddChannel(
        Dictionary<string, double> channels,
        string id,
        double? value)
    {
        if (value is double v && !double.IsNaN(v))
            channels[id] = v;
    }

    private static bool TryReadUInt64(JsonElement obj, string name, out ulong value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var el))
            return false;
        return TryReadUInt64(el, out value);
    }

    private static bool TryReadUInt64(JsonElement el, out ulong value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number)
        {
            if (el.TryGetUInt64(out value))
                return true;
            if (el.TryGetInt64(out var s) && s >= 0)
            {
                value = (ulong)s;
                return true;
            }
        }

        if (el.ValueKind == JsonValueKind.String &&
            ulong.TryParse(
                el.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadInt64(JsonElement el, out long value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number)
        {
            if (el.TryGetInt64(out value))
                return true;
            if (el.TryGetDouble(out var d))
            {
                value = (long)d;
                return true;
            }
        }

        if (el.ValueKind == JsonValueKind.String &&
            long.TryParse(
                el.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        return false;
    }
}
