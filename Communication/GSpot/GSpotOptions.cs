namespace Chassis_Master_Test_Suite.Communication.GSpot;

/// <summary>
/// GSpot（HTTP token + WebSocket 推流）连接参数。
/// </summary>
public sealed class GSpotOptions
{
    /// <summary>HTTP API 根地址，不含末尾斜杠。</summary>
    public string BaseUrl { get; init; } =
        "https://weixin.jichexiaozi.com/transponder";

    /// <summary>房间短号（room_short_id）。</summary>
    public required string RoomId { get; init; }

    /// <summary>房间密码。</summary>
    public required string Password { get; init; }

    /// <summary>
    /// 可选：只接收该 GUserId。为 null 时锁定首辆出现的车。
    /// </summary>
    public string? FilterGUserId { get; init; }

    /// <summary>
    /// 可选：只接收该车编号（cNum）。为 null 时锁定首辆出现的车。
    /// </summary>
    public string? FilterCNum { get; init; }

    /// <summary>
    /// 断线后首次重连等待（毫秒）。之后指数退避直至 MaxReconnectDelayMs。
    /// </summary>
    public int InitialReconnectDelayMs { get; init; } = 1_000;

    /// <summary>重连等待上限（毫秒）。</summary>
    public int MaxReconnectDelayMs { get; init; } = 30_000;

    /// <summary>
    /// 若设置，每条原始 WS body JSON 追加写入该文件（标定用）。
    /// 正式使用请保持 null，避免磁盘膨胀与敏感信息落盘。
    /// </summary>
    public string? RawBodyLogPath { get; init; }
}
