namespace Chassis_Master_Test_Suite.Communication.Remote;

/// <summary>
/// CMTS remote Android device (protocol v0.1) connection options.
/// Default endpoint: ws://IP:8787/cmts/v1 — no TLS in v0.1.
/// </summary>
public sealed class CMTSRemoteOptions
{
    /// <summary>Device IP or hostname (manual entry for v0.1).</summary>
    public required string Host { get; init; }

    /// <summary>WebSocket port. Protocol default 8787.</summary>
    public int Port { get; init; } = 8787;

    /// <summary>WebSocket path. Protocol default /cmts/v1.</summary>
    public string Path { get; init; } = "/cmts/v1";

    /// <summary>Pairing token (optional empty until Android requires it).</summary>
    public string Token { get; init; } = "";

    /// <summary>Client version string sent in device.hello.</summary>
    public string ClientVer { get; init; } = "0.3.1";

    /// <summary>stream.subscribe maxHz (core channel).</summary>
    public int MaxHz { get; init; } = 100;

    public string? ExpectedDeviceId { get; init; }

    public int InitialReconnectDelayMs { get; init; } = 1_000;

    public int MaxReconnectDelayMs { get; init; } = 30_000;

    /// <summary>Hello / record command response timeout.</summary>
    public int CommandTimeoutMs { get; init; } = 8_000;

    public string BuildWebSocketUrl()
    {
        var path = Path.StartsWith('/') ? Path : "/" + Path;
        return $"ws://{Host.Trim()}:{Port}{path}";
    }
}
