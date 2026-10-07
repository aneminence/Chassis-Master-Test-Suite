using Chassis_Master_Test_Suite.Core;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace Chassis_Master_Test_Suite.Recorder;

/// <summary>
/// CMTS 异步 CSV 数据记录器。
///
/// 数据首先进入内存队列，
/// 后台任务负责写入磁盘。
/// Recorder 自己负责 Flush 和关闭。
/// </summary>
public sealed class CsvRecorder : IDisposable
{
    private readonly StreamWriter _writer;

    private readonly Channel<VehicleSample> _channel;

    private readonly Task _writerTask;

    /// <summary>
    /// 用于按真实时间控制 Flush 间隔，
    /// 不再依赖采样频率。
    /// </summary>
    private readonly Stopwatch _flushStopwatch = new();

    public CsvRecorder(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(
            filePath,
            false,
            new UTF8Encoding(true));

        WriteHeader();

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

    private void WriteHeader()
    {
        _writer.WriteLine(
            "Timestamp,Sequence,SpeedKph," +
            "LongitudinalAcceleration," +
            "LateralAcceleration," +
            "VerticalAcceleration," +
            "YawRate,Latitude,Longitude," +
            "Altitude,Heading");
    }

    /// <summary>
    /// 将数据放入记录队列。
    /// 不直接执行磁盘写入。
    /// </summary>
    public bool TryWrite(VehicleSample sample)
    {
        return _channel.Writer.TryWrite(sample);
    }

    /// <summary>
    /// 后台 CSV 写入循环。
    ///
    /// 退出方式：
    /// Dispose() 调用 _channel.Writer.TryComplete()，
    /// 本循环先把队列里剩余数据全部写完，
    /// 然后 WaitToReadAsync 返回 false，循环自然结束。
    ///
    /// 这里刻意不使用 CancellationToken：
    /// 提前取消会丢掉队列中还没落盘的数据，
    /// 而原始数据不允许丢。
    /// </summary>
    private async Task WriteLoopAsync()
    {
        var reader =
            _channel.Reader;

        while (await reader.WaitToReadAsync())
        {
            while (reader.TryRead(
                       out var sample))
            {
                WriteSample(sample);
            }
        }

        // 队列已排空，做一次最终 Flush。
        _writer.Flush();
    }

    /// <summary>
    /// 将一条 VehicleSample 转换为 CSV。
    /// </summary>
    private void WriteSample(VehicleSample sample)
    {
        var line = string.Join(",",
            sample.Timestamp.ToString(
                CultureInfo.InvariantCulture),
            sample.Sequence.ToString(
                CultureInfo.InvariantCulture),
            sample.SpeedKph.ToString(
                "F6",
                CultureInfo.InvariantCulture),
            sample.LongitudinalAcceleration.ToString(
                "F6",
                CultureInfo.InvariantCulture),
            sample.LateralAcceleration.ToString(
                "F6",
                CultureInfo.InvariantCulture),
            sample.VerticalAcceleration.ToString(
                "F6",
                CultureInfo.InvariantCulture),
            sample.YawRate.ToString(
                "F6",
                CultureInfo.InvariantCulture),
            sample.Latitude.ToString(
                "F8",
                CultureInfo.InvariantCulture),
            sample.Longitude.ToString(
                "F8",
                CultureInfo.InvariantCulture),
            sample.Altitude.ToString(
                "F6",
                CultureInfo.InvariantCulture),
            sample.Heading.ToString(
                "F6",
                CultureInfo.InvariantCulture));

        _writer.WriteLine(line);

        // 按时间 Flush，而不是按序号。
        //
        // 旧代码用 sample.Sequence % 200 == 0，
        // 隐含假设 200 Hz。
        // 现在频率可能变化，改用真实时间判断：
        // 每约 1 秒主动 Flush 一次。
        if (_flushStopwatch.ElapsedMilliseconds >= 1000)
        {
            _writer.Flush();

            _flushStopwatch.Restart();
        }
    }

    /// <summary>
    /// 停止后台写入并等待队列中的数据全部写入磁盘。
    /// </summary>
    public void Dispose()
    {
        // 不再接受新数据。
        //
        // 这会通知后台循环：
        // 把队列里剩下的数据写完之后自然退出。
        _channel.Writer.TryComplete();

        try
        {
            // 等待后台任务把队列中的数据全部写完。
            _writerTask.Wait(TimeSpan.FromSeconds(15));
        }
        catch
        {
            // 后台任务出现异常时，
            // 仍然继续做最终 Flush，保住已有数据。
        }

        _writer.Flush();
        _writer.Dispose();
    }
}