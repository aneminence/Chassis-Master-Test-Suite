using System.Threading.Channels;

namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// CMTS 实时车辆数据总线。
/// 用于在数据生产者和消费者之间传递 VehicleSample。
/// </summary>
public sealed class DataBus
{
    private readonly Channel<VehicleSample> _channel;

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

    /// <summary>
    /// 写入一条车辆数据。
    /// </summary>
    public bool TryPublish(VehicleSample sample)
    {
        return _channel.Writer.TryWrite(sample);
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