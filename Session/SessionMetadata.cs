using System.Text;

namespace Chassis_Master_Test_Suite.Session;

/// <summary>
/// VBO [SessionData] 键值。空字段仍写出 "Key:" 以兼容 Racelogic 习惯。
/// </summary>
public sealed class SessionMetadata
{
    public string TimeZone { get; set; } = TimeZoneInfo.Local.Id;
    public string TestTrack { get; set; } = "";
    public string TestFacility { get; set; } = "";
    public string Comments { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string VehicleNumber { get; set; } = "";
    public string VehicleModel { get; set; } = "";
    public string Weather { get; set; } = "";
    public string Temperature { get; set; } = "";
    public string WindSpeed { get; set; } = "";

    /// <summary>当前会话元数据（录制 / 打开 VBO 共用）。</summary>
    public static SessionMetadata Current { get; set; } = new();

    public SessionMetadata Clone() => new()
    {
        TimeZone = TimeZone,
        TestTrack = TestTrack,
        TestFacility = TestFacility,
        Comments = Comments,
        DriverName = DriverName,
        VehicleNumber = VehicleNumber,
        VehicleModel = VehicleModel,
        Weather = Weather,
        Temperature = Temperature,
        WindSpeed = WindSpeed
    };

    public IEnumerable<string> ToVboLines()
    {
        yield return $"timeZone:{TimeZone}";
        yield return $"TestTrack:{TestTrack}";
        yield return $"TestFacility:{TestFacility}";
        yield return $"Comments:{Comments}";
        yield return $"DriverName:{DriverName}";
        yield return $"VehicleNumber:{VehicleNumber}";
        yield return $"VehicleModel:{VehicleModel}";
        yield return $"Weather:{Weather}";
        yield return $"Temperature:{Temperature}";
        yield return $"WindSpeed:{WindSpeed}";
    }

    public static SessionMetadata FromVboLines(IEnumerable<string> lines)
    {
        var m = new SessionMetadata();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            switch (key.ToLowerInvariant())
            {
                case "timezone": m.TimeZone = value; break;
                case "testtrack": m.TestTrack = value; break;
                case "testfacility": m.TestFacility = value; break;
                case "comments": m.Comments = value; break;
                case "drivername": m.DriverName = value; break;
                case "vehiclenumber": m.VehicleNumber = value; break;
                case "vehiclemodel": m.VehicleModel = value; break;
                case "weather": m.Weather = value; break;
                case "temperature": m.Temperature = value; break;
                case "windspeed": m.WindSpeed = value; break;
            }
        }

        return m;
    }

    public string SummaryLine()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(DriverName)) parts.Add(DriverName);
        if (!string.IsNullOrWhiteSpace(VehicleModel)) parts.Add(VehicleModel);
        else if (!string.IsNullOrWhiteSpace(VehicleNumber)) parts.Add(VehicleNumber);
        if (!string.IsNullOrWhiteSpace(TestTrack)) parts.Add(TestTrack);
        else if (!string.IsNullOrWhiteSpace(TestFacility)) parts.Add(TestFacility);
        return parts.Count == 0 ? "(empty session)" : string.Join(" · ", parts);
    }
}
