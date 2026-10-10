using System.Threading.Channels;

namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// CMTS 实时车辆数据总线。
/// 用于在数据生产者和消费者之间传递 VehicleSample。
/// </summary>
public sealed class DataBus
{
    private readonly Channel<VehicleSample> _channel;

    /// <summary>
    /// TryPublish / TryWrite 返回 false 的次数。
    /// 当前 FullMode=DropOldest 时 TryWrite 通常恒成功，计数多保持 0；
    /// 若日后改成 Wait/DropWrite，可直接观察背压丢弃。
    /// </summary>
    private long _droppedPublishCount;

    public DataBus(int capacity = 2000)
    {
        _channel = Channel.CreateBounded<VehicleSample>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = false
            });
    }

    /// <summary>总线 TryPublish 失败次数（见字段注释）。</summary>
    public long DroppedPublishCount =>
        Interlocked.Read(ref _droppedPublishCount);

    /// <summary>
    /// 写入一条车辆数据。
    /// </summary>
    public bool TryPublish(VehicleSample sample)
    {
        if (_channel.Writer.TryWrite(sample))
            return true;

        Interlocked.Increment(ref _droppedPublishCount);
        return false;
    }

    /// <summary>
    /// 获取数据读取器。
    /// </summary>
    public ChannelReader<VehicleSample> Reader => _channel.Reader;

    /// <summary>
    /// 完成数据总线。
    /// </summary>
    public void Complete()
    {
        _channel.Writer.TryComplete();
    }
}
