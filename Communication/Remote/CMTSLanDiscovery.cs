using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
namespace Chassis_Master_Test_Suite.Communication.Remote;

public sealed record CMTSDiscoveredDevice(string DeviceId, string Label, string Host, int Port, string Path, string? DeviceModel = null, bool AuthRequired = true)
{
    public string DisplayName => string.IsNullOrWhiteSpace(DeviceModel) ? "Android 手机" : DeviceModel;
    public override string ToString() => DisplayName;
}

public static class CMTSLanDiscovery
{
    public static async Task<IReadOnlyList<CMTSDiscoveredDevice>> SearchAsync(IEnumerable<string>? knownHosts, CancellationToken ct)
    {
        var hosts = new HashSet<string>(knownHosts ?? []);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var gateway in nic.GetIPProperties().GatewayAddresses)
                if (gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(System.Net.IPAddress.Any))
                    hosts.Add(gateway.Address.ToString());
        }
        var candidates = hosts.Where(h => !string.IsNullOrWhiteSpace(h)).Take(32).Select(h => (Host: h, Port: 8787, Path: "/cmts/v1")).ToList();
        candidates.AddRange(await CMTSMdnsDiscovery.DiscoverAsync(TimeSpan.FromSeconds(3), ct));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        using var limit = new SemaphoreSlim(4);
        var results = await Task.WhenAll(candidates.Distinct().Take(64).Select(async candidate =>
        {
            var entered = false;
            try
            {
                await limit.WaitAsync(deadline.Token);
                entered = true;
                return await ProbeAsync(candidate.Host, candidate.Port, candidate.Path, deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
            finally { if (entered) limit.Release(); }
        }));
        return results.OfType<CMTSDiscoveredDevice>().DistinctBy(d => d.DeviceId).ToArray();
    }

    public static async Task<CMTSDiscoveredDevice?> ProbeAsync(string host, int port, string path, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new UriBuilder("ws", host, port, path).Uri, timeout.Token);
            var id = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.SerializeToUtf8Bytes(new { v = 1, type = "device.discover", id, payload = new { } });
            await ws.SendAsync(request.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
            var data = new byte[8192];
            var used = 0;
            ValueWebSocketReceiveResult part;
            do
            {
                if (used == data.Length) return null;
                part = await ws.ReceiveAsync(data.AsMemory(used), timeout.Token);
                if (part.MessageType != WebSocketMessageType.Text) return null;
                used += part.Count;
            } while (!part.EndOfMessage);
            return ParseResponse(Encoding.UTF8.GetString(data, 0, used), id, host, port, path);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ArgumentException or JsonException or InvalidOperationException)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    public static CMTSDiscoveredDevice? ParseResponse(string json, string requestId, string host, int port, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var p = r.GetProperty("payload");
            if (r.GetProperty("v").GetInt32() != 1 || r.GetProperty("type").GetString() != "device.discover" ||
                r.GetProperty("id").GetString() != requestId || p.GetProperty("discoveryVersion").GetInt32() != 1 ||
                p.GetProperty("deviceType").GetString() != "cmts-android" ||
                p.GetProperty("protoMin").GetInt32() > 1 || p.GetProperty("protoMax").GetInt32() < 1 ||
                p.GetProperty("port").GetInt32() != port || p.GetProperty("path").GetString() != path) return null;
            var deviceId = r.GetProperty("deviceId").GetString();
            var label = p.GetProperty("deviceLabel").GetString();
            if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 128 || label?.Length > 128) return null;
            var model = p.TryGetProperty("deviceModel", out var modelValue) && modelValue.ValueKind == JsonValueKind.String ? modelValue.GetString() : null;
            if (model?.Length > 128) return null;
            return new(deviceId, label ?? "CMTS Android", host, port, path, model, p.GetProperty("authRequired").GetBoolean());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { return null; }
    }
}
