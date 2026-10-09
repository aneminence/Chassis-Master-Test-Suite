namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// 通道目录 + 当前可用集合。
///
/// 目录以真实 Racelogic 导出（ons shot 7.vbo）的 [column names] 为基准；
/// UI 下拉只展示 <see cref="Available"/>，绝不列出当前数据集没有的通道。
/// </summary>
public sealed class ChannelRegistry
{
    /// <summary>合成时间轴，始终可选作 X。</summary>
    /// <remarks>
    /// 必须声明在 <see cref="Instance"/> 之前：C# 静态字段按文本顺序初始化，
    /// 若 Instance 先 new()，构造里 SetLiveCore 读到的 AxisTime 仍是 null，
    /// 会把空槽塞进 Available，启动时 DefaultPlotChannelId/IsAvailable 直接 NRE。
    /// </remarks>
    public static ChannelInfo AxisTime { get; } =
        new(ChannelIds.AxisTime, "Time", "Beijing", isSyntheticTime: true);

    public static ChannelRegistry Instance { get; } = new();

    private readonly Dictionary<string, ChannelInfo> _catalog =
        new(StringComparer.OrdinalIgnoreCase);

    private List<ChannelInfo> _available = new();

    private ChannelRegistry()
    {
        SeedCatalogFromOnsShot7();
        SetLiveCore();
    }

    /// <summary>目录中全部已知通道（含 live 核心与 VBO 扩展）。</summary>
    public IReadOnlyCollection<ChannelInfo> Catalog => _catalog.Values;

    /// <summary>当前可选通道（含合成 Time）。</summary>
    public IReadOnlyList<ChannelInfo> Available => _available;

    /// <summary>可用于曲线 Y 轴的通道（排除合成 Time）。</summary>
    public IReadOnlyList<ChannelInfo> AvailablePlotChannels =>
        _available.Where(c => c is { IsSyntheticTime: false }).ToList();

    /// <summary>默认 Y 通道（优先 velocity）。</summary>
    public string DefaultPlotChannelId
    {
        get
        {
            if (IsAvailable(ChannelIds.Velocity))
                return ChannelIds.Velocity;

            var first = AvailablePlotChannels.FirstOrDefault();
            return first?.Id ?? ChannelIds.Velocity;
        }
    }

    public event EventHandler? AvailableChanged;

    public bool IsAvailable(string channelId) =>
        channelId is not null &&
        _available.Any(c =>
            c is not null &&
            string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase));

    public bool TryGet(string channelId, out ChannelInfo info)
    {
        var match = _available.FirstOrDefault(c =>
            c is not null &&
            string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            info = match;
            return true;
        }

        if (_catalog.TryGetValue(channelId, out info!))
            return true;

        info = new ChannelInfo(channelId, Humanize(channelId), "");
        return false;
    }

    public string GetDisplayName(string channelId) =>
        TryGet(channelId, out var info) ? info.DisplayName : Humanize(channelId);

    public string GetUnit(string channelId) =>
        TryGet(channelId, out var info) ? info.Unit : "";

    /// <summary>实时 UDP / GSpot / Simulator 仅有核心通道。</summary>
    public void SetLiveCore()
    {
        var ids = new[]
        {
            ChannelIds.Velocity,
            ChannelIds.Longacc,
            ChannelIds.Latacc,
            ChannelIds.ZAccel,
            ChannelIds.YawRate,
            ChannelIds.Heading,
            ChannelIds.Latitude,
            ChannelIds.Longitude,
            ChannelIds.Height
        };

        SetAvailable(ids);
    }

    /// <summary>
    /// 按已加载 VBO 的 [column names] 设置可用通道。
    /// 文件里没有的通道不会出现在下拉中。
    /// </summary>
    public void SetFromVboColumns(IEnumerable<string> columnNames)
    {
        var ids = columnNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        SetAvailable(ids);
    }

    private void SetAvailable(IEnumerable<string> channelIds)
    {
        var list = new List<ChannelInfo>();
        if (AxisTime is not null)
            list.Add(AxisTime);

        foreach (var id in channelIds)
        {
            if (string.Equals(id, ChannelIds.AxisTime, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!_catalog.TryGetValue(id, out var info))
            {
                info = new ChannelInfo(id, Humanize(id), GuessUnit(id));
                _catalog[id] = info;
            }

            list.Add(info);
        }

        _available = list;
        AvailableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 用核心物理量填充 Channels 字典（live 源与 VBO 映射共用）。
    /// </summary>
    public static Dictionary<string, double> BuildCoreChannelMap(
        double speedKph,
        double longitudinalAcceleration,
        double lateralAcceleration,
        double verticalAcceleration,
        double yawRate,
        double heading,
        double latitude,
        double longitude,
        double altitude)
    {
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [ChannelIds.Velocity] = speedKph,
            [ChannelIds.Longacc] = longitudinalAcceleration,
            [ChannelIds.Latacc] = lateralAcceleration,
            [ChannelIds.ZAccel] = verticalAcceleration,
            [ChannelIds.YawRate] = yawRate,
            [ChannelIds.Heading] = heading,
            [ChannelIds.Latitude] = latitude,
            [ChannelIds.Longitude] = longitude,
            [ChannelIds.Height] = altitude
        };
    }

    private void SeedCatalogFromOnsShot7()
    {
        // 列名来自 D:\CMTS\ons shot 7.vbo 的 [column names]（58 列）。
        // 单位：已知的按 CMTS 内部约定；VBO [channel units] 段不可信，不照抄。
        void Add(string id, string display, string unit) =>
            _catalog[id] = new ChannelInfo(id, display, unit);

        Add("sats", "Satellites", "count");
        Add("time", "VBO Time", "HHMMSS");
        Add(ChannelIds.Latitude, "Latitude", "deg");
        Add(ChannelIds.Longitude, "Longitude", "deg");
        Add(ChannelIds.Velocity, "Speed", "km/h");
        Add(ChannelIds.Heading, "Heading", "deg");
        Add(ChannelIds.Height, "Altitude", "m");
        Add("vert-vel", "Vertical Velocity", "km/h");
        Add(ChannelIds.Longacc, "Longitudinal Accel", "m/s²");
        Add(ChannelIds.Latacc, "Lateral Accel", "m/s²");
        Add("Solution_Type", "Solution Type", "");
        Add("Velocity_Quality", "Velocity Quality", "");
        Add("event-1", "Event 1", "s");
        Add("Exported_Elapsed_time_2", "Elapsed Time 2", "s");
        Add("Exported_Distance_2", "Distance 2", "m");
        Add("Exported_Lateral_acceleration_2", "Lateral Accel 2", "g");
        Add("Exported_Longitudinal_acceleration_2", "Longitudinal Accel 2", "g");
        Add("Exported_Relative_height_2", "Relative Height 2", "m");
        Add("Exported_Gradient_2", "Gradient 2", "%");
        Add("Exported_Radius_of_turn_2", "Radius of Turn 2", "m");
        Add("Exported_Elapsed_time", "Elapsed Time", "s");
        Add("Exported_Distance", "Distance", "m");
        Add("Exported_Lateral_acceleration", "Lateral Accel (Exp)", "g");
        Add("Exported_Longitudinal_acceleration", "Longitudinal Accel (Exp)", "g");
        Add("Exported_Relative_height", "Relative Height", "m");
        Add("Exported_Gradient", "Gradient", "%");
        Add("Exported_Radius_of_turn", "Radius of Turn", "m");
        Add("InternalAD2", "Internal AD2", "");
        Add("DualStatus", "Dual Status", "");
        Add("True_Head", "True Heading", "deg");
        Add("Lat._Vel.", "Lateral Velocity", "km/h");
        Add("Lng._Vel.", "Longitudinal Velocity", "km/h");
        Add("RobotHead", "Robot Heading", "deg");
        Add(ChannelIds.YawRate, "Yaw Rate", "deg/s");
        Add("Slip_Angle", "Slip Angle", "deg");
        Add("Pos_X", "Pos X", "m");
        Add("Pos_Y", "Pos Y", "m");
        Add("Pos_Z", "Pos Z", "m");
        Add("YawRate", "Yaw Rate (IMU)", "deg/s");
        Add("X_Accel", "X Accel", "m/s²");
        Add("Y_Accel", "Y Accel", "m/s²");
        Add("Temp", "Temperature", "°C");
        Add("PitchRate", "Pitch Rate", "deg/s");
        Add("RollRate", "Roll Rate", "deg/s");
        Add(ChannelIds.ZAccel, "Vertical Accel", "m/s²");
        Add("ADC_CH2", "ADC CH2", "V");
        Add("IMU_Kalman_Filter_Status", "IMU Kalman Status", "");
        Add("Exported_CustomBrakeTrigger", "Custom Brake Trigger", "");
        Add("Exported_Speed.", "Speed (Maths)", "km/h");
        Add("Exported_Lat_Acc_Smoothed", "Lat Acc Smoothed", "g");
        Add("Exported_Centre_Line_Deviation", "Centre Line Deviation", "m");
        Add("Exported_CustomBrakeTrigger_2", "Custom Brake Trigger 2", "");
        Add("Exported_Speed._2", "Speed Maths 2", "km/h");
        Add("Exported_Lat_Acc_Smoothed_2", "Lat Acc Smoothed 2", "g");
        Add("Exported_Centre_Line_Deviation_2", "Centre Line Deviation 2", "m");
        Add("Exported_Y_Accel_Smoothed", "Y Accel Smoothed", "g");
        Add("Exported_X_Accel_Smoothed", "X Accel Smoothed", "g");
        Add("Exported_Long_Acc_Smoothed", "Long Acc Smoothed", "g");
    }

    private static string Humanize(string id)
    {
        return id
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace('.', ' ')
            .Trim();
    }

    private static string GuessUnit(string id)
    {
        if (id.Contains("accel", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Accel", StringComparison.OrdinalIgnoreCase) ||
            id.EndsWith("acc", StringComparison.OrdinalIgnoreCase))
            return "g";

        if (id.Contains("speed", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("vel", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("velocity", StringComparison.OrdinalIgnoreCase))
            return "km/h";

        if (id.Contains("Rate", StringComparison.OrdinalIgnoreCase))
            return "deg/s";

        if (id.Contains("head", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Angle", StringComparison.OrdinalIgnoreCase))
            return "deg";

        if (id.Contains("height", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Distance", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("Pos_", StringComparison.OrdinalIgnoreCase))
            return "m";

        return "";
    }
}
