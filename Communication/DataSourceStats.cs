namespace Chassis_Master_Test_Suite.Communication;

/// <summary>
/// 数据源接收质量统计，供顶部状态栏等 UI 刷新。
///
/// 对无序号的源（如 GSpot），Lost / OutOfOrder 可为 0 或 N/A 语义由 UI 决定。
/// </summary>
public readonly struct DataSourceStats
{
    public DataSourceStats(
        long received,
        long valid,
        long lost,
        long outOfOrder)
    {
        Received = received;
        Valid = valid;
        Lost = lost;
        OutOfOrder = outOfOrder;
    }

    public long Received { get; }
    public long Valid { get; }
    public long Lost { get; }
    public long OutOfOrder { get; }
}
