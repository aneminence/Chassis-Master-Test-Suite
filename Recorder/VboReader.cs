using Chassis_Master_Test_Suite.Core;
using System.Globalization;
using System.IO;

namespace Chassis_Master_Test_Suite.Recorder;

/// <summary>
/// VBO 文件读取器。
///
/// 用于离线回放：把 VBOX 文本格式（.vbo）
/// 解析成统一的 VehicleSample 序列。
///
/// 解析规则（实测自真实的 Racelogic 导出文件）：
/// 1. 按 [段名] 切分文件，只在 [data] 段读取数据。
/// 2. 列定义以 [column names] 为准，不能使用 [header] ——
///    [header] 里的通道名可能自带空格（例如
///    "Exported_Lateral acceleration_2"），
///    会导致列数与实际数据行不一致。
/// 3. [channel units] 段在 Racelogic 导出的文件里
///    与通道完全错位，不可信。
///    单位一律由本类内置的换算规则决定。
/// 4. 数据行为空格分隔的定宽文本。
/// </summary>
public sealed class VboReader
{
    /// <summary>
    /// 标准重力加速度，用于把 VBO 的 g 换算回 m/s²。
    /// </summary>
    private const double StandardGravity = 9.80665;

    /// <summary>
    /// [column names] 中每个数据量对应的下标。
    /// 找不到的列记为 -1。
    /// </summary>
    private readonly Dictionary<string, int> _columnIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _columnIndexBuilt;

    public VboReader(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    /// <summary>
    /// [comments] 段内容，原样保留。
    /// </summary>
    public List<string> Comments { get; } = new();

    /// <summary>
    /// [SessionData] 段内容，原样保留。
    /// </summary>
    public List<string> SessionData { get; } = new();

    /// <summary>
    /// [column names] 段解析出的列名。
    /// </summary>
    public List<string> Columns { get; } = new();

    /// <summary>
    /// 从 [comments] 的 "Log Rate (Hz) : 100.00" 读出的采样率。
    /// 没有该行时为 0。
    /// </summary>
    public double LogRateHz { get; private set; }

    /// <summary>
    /// 解析过程中跳过的、无法解析的数据行数。
    /// </summary>
    public int SkippedDataLines { get; private set; }

    /// <summary>
    /// 读入文件并解析出全部样本。
    ///
    /// 文件规模较大（100 Hz 约 700 字节/秒），
    /// 这里一次性读入内存，
    /// 与 MainWindow 现有的 _sampleHistory 策略保持一致。
    /// </summary>
    public List<VehicleSample> ReadAll()
    {
        var samples = new List<VehicleSample>();

        var lines = File.ReadAllLines(FilePath);

        var section = string.Empty;

        long sequence = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine;

            // 去掉可能存在的 BOM（本程序自己写文件时不写 BOM，
            // 但外部工具导出的文件可能有）。
            if (line.Length > 0 && line[0] == '\uFEFF')
            {
                line = line[1..];
            }

            var trimmed = line.Trim();

            // 空行跳过。
            if (trimmed.Length == 0)
            {
                continue;
            }

            // 段标记。
            if (trimmed.StartsWith('[') &&
                trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1];

                // 进入 [data] 之前先把列下标建好，
                // 否则解析数据行时取不到任何列。
                if (section == "data")
                {
                    BuildColumnIndex();
                }

                continue;
            }

            switch (section)
            {
                case "comments":
                    Comments.Add(trimmed);

                    if (trimmed.StartsWith(
                            "Log Rate",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        LogRateHz = ParseTrailingNumber(trimmed);
                    }

                    break;

                case "SessionData":
                    SessionData.Add(trimmed);
                    break;

                case "column names":
                    // 真实 Racelogic 导出文件把全部列名
                    // 放在同一行，用空格分隔（不是一行一个）。
                    // 这里按空格拆开，最后一项后面可能带空格，
                    // RemoveEmptyEntries 会去掉空项。
                    Columns.AddRange(
                        line.Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries));
                    break;

                case "data":
                    var sample = ParseDataLine(line, sequence);

                    if (sample != null)
                    {
                        samples.Add(sample);
                        sequence++;
                    }
                    else
                    {
                        SkippedDataLines++;
                    }

                    break;
            }
        }

        return samples;
    }

    /// <summary>
    /// 建立列名 -> 下标映射。
    /// 只在首次调用时生效。
    /// </summary>
    private void BuildColumnIndex()
    {
        if (_columnIndexBuilt)
        {
            return;
        }

        _columnIndexBuilt = true;

        for (var i = 0; i < Columns.Count; i++)
        {
            var name = Columns[i];

            if (!_columnIndex.ContainsKey(name))
            {
                _columnIndex[name] = i;
            }
        }
    }

    /// <summary>
    /// 解析一行数据。
    ///
    /// 单位换算（VBO -> CMTS 内部单位）：
    ///     g      -> m/s²  （乘以 9.80665）
    ///     arcmin -> deg   （除以 60）
    /// </summary>
    private VehicleSample? ParseDataLine(
        string line,
        long sequence)
    {
        var tokens = line.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2)
        {
            return null;
        }

        double Get(string name)
        {
            if (!_columnIndex.TryGetValue(name, out var index))
            {
                return 0.0;
            }

            if (index < 0 || index >= tokens.Length)
            {
                return 0.0;
            }

            return double.TryParse(
                tokens[index],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 0.0;
        }

        return new VehicleSample
        {
            Timestamp = ParseTimeColumn(Get("time")),
            Sequence = sequence,

            SpeedKph = Get("velocity"),

            LongitudinalAcceleration =
                Get("Longacc") * StandardGravity,

            LateralAcceleration =
                Get("Latacc") * StandardGravity,

            VerticalAcceleration =
                Get("Z_Accel") * StandardGravity,

            YawRate = Get("Yaw_Rate"),
            Heading = Get("heading"),

            Latitude = Get("lat") / 60.0,

            // Racelogic 的 VBO 里经度符号与常规约定相反：
            // 负值是东经、正值是西经（本文件 -7238.68 角分实际是 120.6446°E，
            // 33.25°N 120.64°E 也正好落在 [SessionData] 声明的 China Standard Time）。
            // 所以这里取负号，把 VehicleSample.Longitude 规范成
            // "正 = 东经、负 = 西经"，跟 Simulator 和以后的地图瓦片保持一致。
            // VboRecorder 写文件时要做同样的反变换。
            Longitude = -Get("long") / 60.0,

            Altitude = Get("height")
        };
    }

    /// <summary>
    /// 把 time 列的 HHMMSS.mmm 解析成
    /// "当天 UTC 起的毫秒数"。
    ///
    /// VBO 的 time 列只有时间没有日期，
    /// 所以只能还原出当天的毫秒数。
    /// 回放时用它计算经过时间，
    /// 不能当作绝对时间使用。
    /// </summary>
    private static long ParseTimeColumn(double timeValue)
    {
        if (double.IsNaN(timeValue) ||
            double.IsInfinity(timeValue))
        {
            return 0;
        }

        if (timeValue < 0)
        {
            timeValue = 0;
        }

        // HHMMSS.mmm
        var hours = (int)(timeValue / 10000.0);

        var minutes = (int)(
            (timeValue - (hours * 10000.0)) / 100.0);

        var seconds =
            timeValue -
            (hours * 10000.0) -
            (minutes * 100.0);

        return (long)(
            (TimeSpan.FromHours(hours) +
             TimeSpan.FromMinutes(minutes) +
             TimeSpan.FromSeconds(seconds)).TotalMilliseconds);
    }

    /// <summary>
    /// 从 "Log Rate (Hz) : 100.00" 这类文本中取出最后的数字。
    /// </summary>
    private static double ParseTrailingNumber(string text)
    {
        var parts = text.Split(
            ':',
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return 0.0;
        }

        return double.TryParse(
            parts[^1].Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0.0;
    }
}
