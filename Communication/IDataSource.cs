namespace Chassis_Master_Test_Suite.Communication;

/// <summary>
/// CMTS 统一数据源接口。
///
/// 实现方负责把原始协议转成 <see cref="Core.VehicleSample"/>，
/// 并通过构造时注入的 <see cref="Core.DataBus"/> 发布；
/// UI / Recorder 只订阅 DataBus，不直接依赖具体协议。
/// </summary>
public interface IDataSource : IDisposable
{
    /// <summary>显示用名称，例如 "UDP"、"GSpot"。</summary>
    string Name { get; }

    /// <summary>当前连接状态。</summary>
    DataSourceState State { get; }

    /// <summary>接收质量统计快照。</summary>
    DataSourceStats Stats { get; }

    /// <summary>状态变更（可能来自后台线程，订阅方需自行切回 UI 线程）。</summary>
    event EventHandler<DataSourceState>? StateChanged;

    /// <summary>
    /// 启动数据源。返回表示“已开始”，不表示接收循环结束。
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止数据源并释放底层资源。可安全重复调用。
    /// </summary>
    Task StopAsync();
}
