namespace Chassis_Master_Test_Suite.Communication;

/// <summary>
/// 数据源连接生命周期状态。
///
/// UDP 无真正的会话：Bind 成功并进入接收循环后即为 Connected，
/// 关闭 socket 后回到 Disconnected。GSpot 等有连接概念的源
/// 会用到 Connecting / Reconnecting / Faulted。
/// </summary>
public enum DataSourceState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Faulted
}
