using Chassis_Master_Test_Suite.Core;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace Chassis_Master_Test_Suite.Recorder;

/// <summary>
/// CMTS 异步 VBO（VBOX 文本格式）数据记录器。
///
/// 与 CsvRecorder 结构一致：
/// 数据先进入内存队列，后台任务负责写磁盘，
/// Recorder 自己负责 Flush 和关闭。
///
/// 输出文件共 6 个段，固定顺序：
///     [header] -> [channel units] -> [comments]
///     -> [SessionData] -> [column names] -> [data]
///
/// 共 11 列 = 2 个时间列 + 9 个数据列：
///     time           UTC 时间，HHMMSS.mmm
///     Elapsed_time   相对"第一条数据"的经过时间，s
///     velocity       车速，km/h
///     Longacc        纵向加速度，g
///     Latacc         横向加速度，g
///     Yaw_Rate       横摆角速度，deg/s
///     heading        航向角，deg
///     lat            纬度，单位：角分（arc minute）
///     long           经度，单位：角分（arc minute）
///     height         海拔，m
///     Z_Accel        垂向加速度，g
///
/// 注意：VBO 格式中经纬度使用"角分"，不是十进制度。
/// 写入时 角分 = 度 * 60，读取时 度 = 角分 / 60。
/// </summary>
public sealed class VboRecorder : IDisposable
{
    /// <summary>
    /// 标准重力加速度。
    /// 用于把 CMTS 内部的 m/s² 换算成 VBO 使用的 g。
    /// </summary>
    private const double StandardGravity = 9.80665;

    /// <summary>
    /// VBO 使用 CRLF 换行。
    /// 显式指定，避免依赖运行平台的默认值。
    /// </summary>
    private const string NewLine = "\r\n";

    /// <summary>
    /// [column names] 段的列短名，顺序与数据行完全一致。
    ///
    /// 这些短名同时是 VboReader 解析时查找的键
    /// （time / velocity / Longacc / Latacc / Yaw_Rate / heading
    ///   / lat / long / height / Z_Accel），
    /// 改这里必须同步改 VboReader，否则读回来全是 0。
    /// </summary>
    private static readonly string[] ColumnNames =
    {
        "time",
        "Elapsed_time",
        "velocity",
        "Longacc",
        "Latacc",
        "Yaw_Rate",
        "heading",
        "lat",
        "long",
        "height",
        "Z_Accel"
    };

    /// <summary>
    /// 与 CsvRecorder 一致：按真实时间 Flush，
    /// 不依赖采样频率（旧代码用 Sequence % 200 隐含假设了 200 Hz）。
    /// </summary>
    private readonly Stopwatch _flushStopwatch = new();

    private readonly StreamWriter _writer;

    private readonly Channel<VehicleSample> _channel;

    private readonly Task _writerTask;

    /// <summary>
    /// 自增行号。
    /// 仅后台写入线程访问，不需要锁。
    /// </summary>
    private long _rowCount;

    /// <summary>
    /// 是否为第一条数据。
    /// 用第一条数据的时间戳作为"经过时间"的零点，
    /// 而不是构造函数调用的时刻，
    /// 这样录制的第一行 Elapsed_time 一定是 0.0000。
    /// </summary>
    private bool _isFirstSample = true;

    /// <summary>
    /// 第一条数据的时间戳（毫秒）。
    /// "经过时间"以此为原点，避免 DateTime 来回转换引入误差。
    /// </summary>
    private long _firstTimestampMilliseconds;

    public VboRecorder(string filePath)
    {
        FilePath = filePath;

        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // VBO 是纯文本、无 BOM。
        // 用 CsvRecorder 的 UTF8Encoding(true) 会在文件头写入 BOM，
        // 某些工具解析第一行时会把它当成内容。
        _writer = new StreamWriter(
            filePath,
            false,
            new UTF8Encoding(false))
        {
            NewLine = NewLine
        };

        WriteFileHeader();

        _flushStopwatch.Start();

        _channel = Channel.CreateBounded<VehicleSample>(
            new BoundedChannelOptions(10000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            });

        _writerTask = WriteLoopAsync();
    }

    /// <summary>
    /// 文件路径，便于日志和调试。
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// 已经写入磁盘的数据行数。
    /// </summary>
    public long RowCount => Interlocked.Read(ref _rowCount);

    /// <summary>
    /// 写入 [header] / [channel units] / [comments]
    /// / [SessionData] / [column names] / [data] 六个段头。
    ///
    /// 这里刻意自己定义列和单位，
    /// 不照抄外部 VBO 文件的 [channel units] ——
    /// 实测 Racelogic 导出的该段会与实际通道错位，不可信。
    /// </summary>
    private void WriteFileHeader()
    {
        // 段 0：标题行
        _writer.WriteLine(
            $"File created on {DateTime.Now:dd/MM/yyyy} @ {DateTime.Now:HH:mm}");
        _writer.WriteLine();

        // 段 1：[header] 通道全名
        //
        // 命名对齐 Racelogic 原厂 VBO 的写法
        // （例如 "velocity kmh" 而不是 "Velocity"），
        // 这样 VBOX Test Suite 的通道映射才认得出来。
        _writer.WriteLine("[header]");
        _writer.WriteLine("time");
        _writer.WriteLine("Elapsed time");
        _writer.WriteLine("velocity kmh");
        _writer.WriteLine("Long accel g");
        _writer.WriteLine("Lat accel g");
        _writer.WriteLine("Yaw rate");
        _writer.WriteLine("heading");
        _writer.WriteLine("latitude");
        _writer.WriteLine("longitude");
        _writer.WriteLine("height");
        _writer.WriteLine("Z accel g");
        _writer.WriteLine();

        // 段 2：[channel units] 单位，与列一一对应
        _writer.WriteLine("[channel units]");
        _writer.WriteLine("s");
        _writer.WriteLine("s");
        _writer.WriteLine("km/h");
        _writer.WriteLine("g");
        _writer.WriteLine("g");
        _writer.WriteLine("deg/s");
        _writer.WriteLine("deg");
        _writer.WriteLine("arcmin");
        _writer.WriteLine("arcmin");
        _writer.WriteLine("m");
        _writer.WriteLine("g");
        _writer.WriteLine();

        // 段 3：[comments] 设备与软件信息
        _writer.WriteLine("[comments]");
        _writer.WriteLine("Chassis Master Test Suite");
        _writer.WriteLine($"Export time : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        _writer.WriteLine("Channel set : 11 (2 time + 9 data)");
        _writer.WriteLine("Lat/Long unit : arc minutes (decimal degrees = value / 60)");
        _writer.WriteLine();

        // 段 4：[SessionData] 会话元数据
        _writer.WriteLine("[SessionData]");
        _writer.WriteLine($"timeZone:{TimeZoneInfo.Local.Id}");
        _writer.WriteLine("TestTrack:");
        _writer.WriteLine("TestFacility:");
        _writer.WriteLine("Comments:");
        _writer.WriteLine("DriverName:");
        _writer.WriteLine("VehicleNumber:");
        _writer.WriteLine("VehicleModel:");
        _writer.WriteLine("Weather:");
        _writer.WriteLine("Temperature:");
        _writer.WriteLine("WindSpeed:");
        _writer.WriteLine();

        // 段 5：[column names] 列短名，解析时以此为准
        //
        // 【关键】全部通道名必须写在“同一行”，用空格分隔 ——
        // 这是 Racelogic VBO 的格式。
        // 曾经写成"一行一个通道名"，结果 VBOX Test Suite 只把
        // 第一行当成一个通道，整个文件的通道表都是坏的，
        // 打开时直接报 "primary channel(s) missing. Speed"。
        //
        // 结尾留一个空格，与原厂文件一致。
        _writer.WriteLine("[column names]");
        _writer.WriteLine(string.Join(" ", ColumnNames) + " ");
        _writer.WriteLine();

        // 段 6：[data] 数据起始标记
        _writer.WriteLine("[data]");
    }

    /// <summary>
    /// 将数据放入记录队列，不直接写磁盘。
    /// </summary>
    public bool TryWrite(VehicleSample sample)
    {
        return _channel.Writer.TryWrite(sample);
    }

    /// <summary>
    /// 后台写入循环。
    ///
    /// 退出方式与 CsvRecorder 相同：
    /// Dispose() 调用 _channel.Writer.TryComplete()，
    /// 本循环把队列里剩余数据全部写完之后自然结束。
    ///
    /// 刻意不使用 CancellationToken：
    /// 提前取消会丢掉队列中还没落盘的数据，
    /// 而原始数据不允许丢。
    /// </summary>
    private async Task WriteLoopAsync()
    {
        var reader = _channel.Reader;

        while (await reader.WaitToReadAsync())
        {
            while (reader.TryRead(out var sample))
            {
                WriteSample(sample);
            }
        }

        // 队列已排空，做一次最终 Flush。
        _writer.Flush();
    }

    /// <summary>
    /// 将一条 VehicleSample 转换为一行 VBO 数据。
    ///
    /// 单位换算集中在这里：
    ///     m/s²  ->  g      （除以 9.80665）
    ///     deg   ->  arcmin （乘以 60）
    /// </summary>
    private void WriteSample(VehicleSample sample)
    {
        // 用第一条数据定义时间零点。
        if (_isFirstSample)
        {
            _isFirstSample = false;

            _firstTimestampMilliseconds = sample.Timestamp;
        }

        // UTC 时间。
        var timeText = ToUtc(sample.Timestamp).ToString(
            "HHmmss.fff",
            CultureInfo.InvariantCulture);

        // 相对"测试开始"的经过时间（秒）。
        var elapsedSeconds =
            (sample.Timestamp - _firstTimestampMilliseconds) / 1000.0;

        var line = string.Join(" ",
            timeText,
            elapsedSeconds.ToString(
                "F4",
                CultureInfo.InvariantCulture),
            sample.SpeedKph.ToString(
                "F3",
                CultureInfo.InvariantCulture),
            (sample.LongitudinalAcceleration / StandardGravity).ToString(
                "F6",
                CultureInfo.InvariantCulture),
            (sample.LateralAcceleration / StandardGravity).ToString(
                "F6",
                CultureInfo.InvariantCulture),
            sample.YawRate.ToString(
                "F6",
                CultureInfo.InvariantCulture),
            sample.Heading.ToString(
                "F6",
                CultureInfo.InvariantCulture),
            (sample.Latitude * 60.0).ToString(
                "F8",
                CultureInfo.InvariantCulture),

            // 取负号：VBO 的经度符号与常规约定相反（详见 VboReader 里的说明）。
            // VehicleSample.Longitude 是"正 = 东经"，
            // VBO 要写成"负 = 东经"，所以这里反过来。
            (-sample.Longitude * 60.0).ToString(
                "F8",
                CultureInfo.InvariantCulture),
            sample.Altitude.ToString(
                "F3",
                CultureInfo.InvariantCulture),
            (sample.VerticalAcceleration / StandardGravity).ToString(
                "F6",
                CultureInfo.InvariantCulture));

        _writer.WriteLine(line);

        Interlocked.Increment(ref _rowCount);

        if (_flushStopwatch.ElapsedMilliseconds >= 1000)
        {
            _writer.Flush();

            _flushStopwatch.Restart();
        }
    }

    /// <summary>
    /// 把 VehicleSample.Timestamp 转成 UTC 时间。
    ///
    /// 该字段由数据源填充，单位是毫秒。
    /// 约定：按 Unix 时间戳（1970-01-01 UTC 起的毫秒数）解释。
    /// </summary>
    private static DateTime ToUtc(long timestampMilliseconds)
    {
        return DateTimeOffset
            .FromUnixTimeMilliseconds(timestampMilliseconds)
            .UtcDateTime;
    }

    /// <summary>
    /// 是否已经释放过。
    /// Dispose() 必须可以重复调用（C# 的 using / 上层重复关闭
    /// 都会触发），否则第二次会抛 ObjectDisposedException。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 停止后台写入，等待队列中数据全部落盘。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 不再接受新数据，通知后台循环排空后退出。
        _channel.Writer.TryComplete();

        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(15));
        }
        catch
        {
            // 后台任务异常时仍然做最终 Flush，保住已有数据。
        }

        _writer.Flush();
        _writer.Dispose();
    }
}
