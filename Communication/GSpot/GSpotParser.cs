using System.Globalization;
using System.Text.Json;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Communication.GSpot;

/// <summary>
/// 按服务端 properties 的 <c>pos</c> 建索引，并把 WS body 数组映射为
/// <see cref="VehicleSample"/>。
///
/// 切勿按固定下标硬编码：实测 properties 带 pos / pos_old，服务端可能调序。
/// </summary>
public sealed class GSpotPropertyIndex
{
    private readonly Dictionary<string, int> _nameToPos =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> NameToPos => _nameToPos;

    /// <summary>
    /// 从 GET /api/motiondata/properties 的 body 数组构建索引。
    /// 每项至少需要 name + pos。
    /// </summary>
    public static GSpotPropertyIndex FromPropertiesBody(
        JsonElement bodyArray)
    {
        if (bodyArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "properties body 不是数组。");
        }

        var index = new GSpotPropertyIndex();

        foreach (var item in bodyArray.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameEl))
                continue;

            var name = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!item.TryGetProperty("pos", out var posEl) ||
                !posEl.TryGetInt32(out var pos))
            {
                continue;
            }

            index._nameToPos[name] = pos;
        }

        if (index._nameToPos.Count == 0)
        {
            throw new InvalidOperationException(
                "properties 中没有任何带 pos 的字段。");
        }

        return index;
    }

    public bool TryGetPos(string name, out int pos) =>
        _nameToPos.TryGetValue(name, out pos);

    /// <summary>订阅用：按 pos 升序拼接 name，避免乱序。</summary>
    public string BuildSubscribePropertiesString()
    {
        return string.Join(
            ',',
            _nameToPos
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key));
    }
}

/// <summary>
/// GSpot WS 帧 → VehicleSample。
/// </summary>
public static class GSpotParser
{
    /// <summary>
    /// 尝试把一条 body 数组映射成样本。
    /// </summary>
    /// <param name="bodyArray">WS JSON 的 body 数组。</param>
    /// <param name="index">按 pos 的属性索引。</param>
    /// <param name="sequence">合成序号（GSpot 无包序号）。</param>
    /// <param name="sample">成功时的样本。</param>
    /// <param name="gUserId">body 中的 GUserId（字符串化）。</param>
    /// <param name="cNum">body 中的 cNum（车编号）。</param>
    /// <returns>能否解析出最少的定位/速度字段。</returns>
    public static bool TryMap(
        JsonElement bodyArray,
        GSpotPropertyIndex index,
        long sequence,
        out VehicleSample? sample,
        out string? gUserId,
        out string? cNum)
    {
        sample = null;
        gUserId = null;
        cNum = null;

        if (bodyArray.ValueKind != JsonValueKind.Array)
            return false;

        gUserId = TryGetString(bodyArray, index, "GUserId")
                  ?? TryGetString(bodyArray, index, "gUserId");
        cNum = TryGetString(bodyArray, index, "cNum")
               ?? TryGetString(bodyArray, index, "CNum");

        // Timestamp：优先 serverTime（Unix 毫秒，已与样例对上）。
        // gpsTime 相对 serverTime 有约 11323 天固定偏移，语义未确认，暂不用。
        if (!TryGetInt64(bodyArray, index, "serverTime", out var serverTime))
        {
            return false;
        }

        TryGetDouble(bodyArray, index, "speed", out var speed);
        TryGetDouble(bodyArray, index, "Speed", out var speedAlt);
        if (speed == 0 && speedAlt != 0)
            speed = speedAlt;

        TryGetDouble(bodyArray, index, "latitude", out var latitude);
        TryGetDouble(bodyArray, index, "Latitude", out var latAlt);
        if (latitude == 0 && latAlt != 0)
            latitude = latAlt;

        TryGetDouble(bodyArray, index, "longitude", out var longitude);
        TryGetDouble(bodyArray, index, "Longitude", out var lonAlt);
        if (longitude == 0 && lonAlt != 0)
            longitude = lonAlt;

        TryGetDouble(bodyArray, index, "altitude", out var altitude);
        TryGetDouble(bodyArray, index, "Altitude", out var altAlt);
        if (altitude == 0 && altAlt != 0)
            altitude = altAlt;

        TryGetDouble(bodyArray, index, "heading", out var heading);
        TryGetDouble(bodyArray, index, "Heading", out var hdgAlt);
        if (heading == 0 && hdgAlt != 0)
            heading = hdgAlt;

        // ---------------------------------------------------------------
        // TODO(calibration): acc / gyro 轴向与单位未由供应商确认。
        // 当前临时约定（仅便于联调，实车标定前不可信）：
        //   acc[0] → LongitudinalAcceleration (假定 m/s²)
        //   acc[1] → LateralAcceleration
        //   acc[2] → VerticalAcceleration
        //   gyro[2] → YawRate (假定 deg/s)；若 gyro 为空则尝试 exts 前 3 元
        // 样例中 gyro 曾为空而 exts 含类陀螺三元组；长度也不稳定。
        // ---------------------------------------------------------------
        double ax = 0, ay = 0, az = 0;
        if (TryGetDoubleArray(bodyArray, index, "acc", out var acc) ||
            TryGetDoubleArray(bodyArray, index, "Acc", out acc))
        {
            if (acc.Length > 0) ax = acc[0];
            if (acc.Length > 1) ay = acc[1];
            if (acc.Length > 2) az = acc[2];
        }

        double yawRate = 0;
        if (TryGetDoubleArray(bodyArray, index, "gyro", out var gyro) ||
            TryGetDoubleArray(bodyArray, index, "Gyro", out gyro))
        {
            if (gyro.Length > 2)
                yawRate = gyro[2];
            else if (gyro.Length == 1)
                yawRate = gyro[0];
        }
        else if (TryGetDoubleArray(bodyArray, index, "exts", out var exts) &&
                 exts.Length >= 3)
        {
            // TODO(calibration): exts 语义不明，仅作 gyro 缺失时的临时回退。
            yawRate = exts[2];
        }

        // TODO(calibration): speed 单位假定为 km/h（与旧客户端展示一致），待确认。
        sample = new VehicleSample
        {
            Timestamp = serverTime,
            Sequence = sequence,
            SpeedKph = speed,
            LongitudinalAcceleration = ax,
            LateralAcceleration = ay,
            VerticalAcceleration = az,
            YawRate = yawRate,
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            Heading = heading,
            Channels = VehicleSample.BuildCoreChannels(
                speed,
                ax,
                ay,
                az,
                yawRate,
                heading,
                latitude,
                longitude,
                altitude)
        };

        return true;
    }

    private static string? TryGetString(
        JsonElement body,
        GSpotPropertyIndex index,
        string name)
    {
        if (!index.TryGetPos(name, out var pos))
            return null;

        if (!TryGetElement(body, pos, out var el))
            return null;

        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.ToString()
        };
    }

    private static bool TryGetInt64(
        JsonElement body,
        GSpotPropertyIndex index,
        string name,
        out long value)
    {
        value = 0;
        if (!index.TryGetPos(name, out var pos))
            return false;

        if (!TryGetElement(body, pos, out var el))
            return false;

        if (el.ValueKind == JsonValueKind.Number &&
            el.TryGetInt64(out value))
        {
            return true;
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

        // 有些实现用 double 传毫秒时间戳
        if (el.ValueKind == JsonValueKind.Number &&
            el.TryGetDouble(out var d))
        {
            value = (long)d;
            return true;
        }

        return false;
    }

    private static bool TryGetDouble(
        JsonElement body,
        GSpotPropertyIndex index,
        string name,
        out double value)
    {
        value = 0;
        if (!index.TryGetPos(name, out var pos))
            return false;

        if (!TryGetElement(body, pos, out var el))
            return false;

        if (el.ValueKind == JsonValueKind.Number &&
            el.TryGetDouble(out value))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.String &&
            double.TryParse(
                el.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetDoubleArray(
        JsonElement body,
        GSpotPropertyIndex index,
        string name,
        out double[] values)
    {
        values = Array.Empty<double>();
        if (!index.TryGetPos(name, out var pos))
            return false;

        if (!TryGetElement(body, pos, out var el))
            return false;

        if (el.ValueKind != JsonValueKind.Array)
            return false;

        var list = new List<double>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number &&
                item.TryGetDouble(out var d))
            {
                list.Add(d);
            }
            else if (item.ValueKind == JsonValueKind.String &&
                     double.TryParse(
                         item.GetString(),
                         NumberStyles.Float,
                         CultureInfo.InvariantCulture,
                         out d))
            {
                list.Add(d);
            }
            else
            {
                list.Add(0);
            }
        }

        values = list.ToArray();
        return true;
    }

    private static bool TryGetElement(
        JsonElement body,
        int pos,
        out JsonElement element)
    {
        element = default;
        if (pos < 0 || pos >= body.GetArrayLength())
            return false;

        element = body[pos];
        return true;
    }
}
