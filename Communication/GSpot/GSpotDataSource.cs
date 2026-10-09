using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Communication.GSpot;

/// <summary>
/// GSpot Telemetry 数据源：
/// HTTP 取 token → properties（按 pos）→ subscribe → WebSocket 实时推流，
/// 过滤多车后写入 <see cref="DataBus"/>。
///
/// 速率由设备事件驱动，非固定 100 Hz；无包序号，Sequence 本地合成。
/// Lost / OutOfOrder 对 GSpot 无协议意义，固定为 0（UI 可显示为 N/A）。
/// </summary>
public sealed class GSpotDataSource : IDataSource
{
    private static readonly HttpClient SharedHttp =
        new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

    private readonly DataBus _dataBus;
    private readonly GSpotOptions _options;

    private long _received;
    private long _valid;
    private long _sequence;

    private DataSourceState _state = DataSourceState.Disconnected;
    private int _started;
    private int _disposed;

    private CancellationTokenSource? _loopCts;
    private Task? _runTask;

    private string? _lockedGUserId;
    private string? _lockedCNum;

    /// <summary>最近一次连接/推流错误（供 UI 显示）。</summary>
    private string? _lastError;

    public GSpotDataSource(
        DataBus dataBus,
        GSpotOptions options)
    {
        _dataBus = dataBus;
        _options = options;

        if (!string.IsNullOrWhiteSpace(options.FilterGUserId))
            _lockedGUserId = options.FilterGUserId;

        if (!string.IsNullOrWhiteSpace(options.FilterCNum))
            _lockedCNum = options.FilterCNum;
    }

    public string Name => "GSpot";

    public DataSourceState State => _state;

    public DataSourceStats Stats => new(
        Interlocked.Read(ref _received),
        Interlocked.Read(ref _valid),
        lost: 0,
        outOfOrder: 0);

    public event EventHandler<DataSourceState>? StateChanged;

    /// <summary>当前锁定的车辆（多车房间里首包或过滤条件）。</summary>
    public string? LockedGUserId => _lockedGUserId;

    public string? LockedCNum => _lockedCNum;

    /// <summary>最近一次失败原因；连接成功时清空。</summary>
    public string? LastError => _lastError;

    public Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _disposed != 0,
            this);

        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(
                ref _started, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;

        SetState(DataSourceState.Connecting);

        _runTask = Task.Run(
            () => RunLoopAsync(token),
            CancellationToken.None);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = _loopCts;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
                // ignore
            }
        }

        var runTask = _runTask;
        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch
            {
                // 循环内已处理
            }
        }

        Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var delayMs = _options.InitialReconnectDelayMs;
        var attempt = 0;

        while (!ct.IsCancellationRequested &&
               _disposed == 0)
        {
            try
            {
                if (attempt == 0)
                    SetState(DataSourceState.Connecting);
                else
                    SetState(DataSourceState.Reconnecting);

                await ConnectAndStreamAsync(
                        ct,
                        onConnected: () =>
                        {
                            _lastError = null;
                            delayMs = _options.InitialReconnectDelayMs;
                            attempt = 0;
                        })
                    .ConfigureAwait(false);

                // 正常返回只可能是取消/停止
                break;
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested ||
                    _disposed != 0)
                {
                    break;
                }

                _lastError = ex.Message;
                SetState(DataSourceState.Reconnecting);
                attempt++;

                try
                {
                    await Task.Delay(delayMs, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                delayMs = Math.Min(
                    delayMs * 2,
                    _options.MaxReconnectDelayMs);
            }
        }

        if (_disposed != 0 || ct.IsCancellationRequested)
        {
            SetState(DataSourceState.Disconnected);
        }
        else
        {
            SetState(DataSourceState.Faulted);
        }

        Interlocked.Exchange(ref _started, 0);
    }

    private async Task ConnectAndStreamAsync(
        CancellationToken ct,
        Action? onConnected = null)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');

        // ---- 1) token ----
        var tokenUrl =
            $"{baseUrl}/api/token" +
            $"?password={Uri.EscapeDataString(_options.Password)}" +
            $"&room_short_id={Uri.EscapeDataString(_options.RoomId)}";

        using var tokenResponse =
            await SharedHttp.GetAsync(tokenUrl, ct)
                .ConfigureAwait(false);

        tokenResponse.EnsureSuccessStatusCode();

        await using var tokenStream =
            await tokenResponse.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);

        using var tokenDoc =
            await JsonDocument.ParseAsync(
                    tokenStream,
                    cancellationToken: ct)
                .ConfigureAwait(false);

        var tokenRoot = tokenDoc.RootElement;
        EnsureApiOk(tokenRoot, "GSpot 登录(token)");
        if (!tokenRoot.TryGetProperty("body", out var tokenBody))
        {
            throw new InvalidOperationException(
                "GSpot 登录失败：响应缺少 body。");
        }

        var accessToken = tokenBody.ValueKind switch
        {
            JsonValueKind.String => tokenBody.GetString(),
            _ => tokenBody.ToString()
        };

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "GSpot 登录失败：token 为空。");
        }

        // ---- 2) properties ----
        using var propsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/api/motiondata/properties");
        propsRequest.Headers.TryAddWithoutValidation(
            "token",
            accessToken);

        using var propsResponse =
            await SharedHttp.SendAsync(propsRequest, ct)
                .ConfigureAwait(false);

        propsResponse.EnsureSuccessStatusCode();

        await using var propsStream =
            await propsResponse.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);

        using var propsDoc =
            await JsonDocument.ParseAsync(
                    propsStream,
                    cancellationToken: ct)
                .ConfigureAwait(false);

        var propsRoot = propsDoc.RootElement;
        EnsureApiOk(propsRoot, "获取 properties");
        if (!propsRoot.TryGetProperty("body", out var propsBody))
        {
            throw new InvalidOperationException(
                "获取 properties 失败：响应缺少 body。");
        }

        var index =
            GSpotPropertyIndex.FromPropertiesBody(propsBody);

        var propertiesCsv =
            index.BuildSubscribePropertiesString();

        // ---- 3) subscribe ----
        var subscribeUrl =
            $"{baseUrl}/api/motiondata/properties/subscribe" +
            $"?properties={Uri.EscapeDataString(propertiesCsv)}";

        using var subRequest = new HttpRequestMessage(
            HttpMethod.Post,
            subscribeUrl);
        subRequest.Headers.TryAddWithoutValidation(
            "token",
            accessToken);

        using var subResponse =
            await SharedHttp.SendAsync(subRequest, ct)
                .ConfigureAwait(false);

        subResponse.EnsureSuccessStatusCode();

        await using var subStream =
            await subResponse.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);

        using var subDoc =
            await JsonDocument.ParseAsync(
                    subStream,
                    cancellationToken: ct)
                .ConfigureAwait(false);

        var subRoot = subDoc.RootElement;
        EnsureApiOk(subRoot, "subscribe");
        if (!subRoot.TryGetProperty("body", out var subBody) ||
            !subBody.TryGetProperty("wsPath", out var wsPathEl))
        {
            throw new InvalidOperationException(
                "subscribe 失败：缺少 wsPath。");
        }

        var wsPath = wsPathEl.GetString();
        if (string.IsNullOrWhiteSpace(wsPath))
        {
            throw new InvalidOperationException(
                "subscribe 失败：wsPath 为空。");
        }

        var wsUrl = BuildWebSocketUrl(baseUrl, wsPath);

        // ---- 4) WebSocket ----
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(wsUrl), ct)
            .ConfigureAwait(false);

        SetState(DataSourceState.Connected);
        onConnected?.Invoke();

        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();

        while (ws.State == WebSocketState.Open &&
               !ct.IsCancellationRequested)
        {
            message.SetLength(0);

            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(
                        buffer,
                        ct)
                    .ConfigureAwait(false);

                if (result.MessageType ==
                    WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(
                            WebSocketCloseStatus
                                .NormalClosure,
                            "server closed",
                            CancellationToken.None)
                        .ConfigureAwait(false);

                    throw new IOException(
                        "GSpot WebSocket 被服务端关闭。");
                }

                message.Write(
                    buffer,
                    0,
                    result.Count);
            }
            while (!result.EndOfMessage);

            Interlocked.Increment(ref _received);

            var jsonBytes = message.ToArray();
            HandleMessage(jsonBytes, index);
        }

        if (!ct.IsCancellationRequested)
        {
            throw new IOException(
                "GSpot WebSocket 意外断开。");
        }
    }

    private void HandleMessage(
        byte[] jsonBytes,
        GSpotPropertyIndex index)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBytes);
            var root = doc.RootElement;

            if (!TryGetStatusOk(root) ||
                !root.TryGetProperty("body", out var body))
            {
                return;
            }

            // body 可能是单帧数组，也可能是多帧数组的数组
            if (body.ValueKind == JsonValueKind.Array &&
                body.GetArrayLength() > 0 &&
                body[0].ValueKind == JsonValueKind.Array)
            {
                foreach (var frame in body.EnumerateArray())
                {
                    ProcessBodyArray(frame, index, jsonBytes);
                }
            }
            else if (body.ValueKind == JsonValueKind.Array)
            {
                ProcessBodyArray(body, index, jsonBytes);
            }
        }
        catch (JsonException)
        {
            // 坏帧跳过
        }
    }

    private void ProcessBodyArray(
        JsonElement bodyArray,
        GSpotPropertyIndex index,
        byte[] rawJson)
    {
        var seq = Interlocked.Increment(ref _sequence);

        if (!GSpotParser.TryMap(
                bodyArray,
                index,
                seq,
                out var sample,
                out var gUserId,
                out var cNum) ||
            sample is null)
        {
            return;
        }

        if (!PassVehicleFilter(gUserId, cNum))
            return;

        MaybeLogRaw(rawJson);

        Interlocked.Increment(ref _valid);
        _dataBus.TryPublish(sample);
    }

    private bool PassVehicleFilter(
        string? gUserId,
        string? cNum)
    {
        // 尚未锁定：用首辆有效车锁定（或已配置的过滤条件）
        if (_lockedGUserId is null && _lockedCNum is null)
        {
            _lockedGUserId = gUserId;
            _lockedCNum = cNum;
            return true;
        }

        if (_lockedGUserId is not null &&
            !string.Equals(
                _lockedGUserId,
                gUserId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (_lockedCNum is not null &&
            !string.Equals(
                _lockedCNum,
                cNum,
                StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private void MaybeLogRaw(byte[] rawJson)
    {
        var path = _options.RawBodyLogPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var line = Encoding.UTF8.GetString(rawJson);
            File.AppendAllText(
                path,
                line + Environment.NewLine);
        }
        catch
        {
            // 落盘失败不影响主路径
        }
    }

    private static string BuildWebSocketUrl(
        string httpBaseUrl,
        string wsPath)
    {
        // 从 BaseUrl 推导 wss/ws，并保留 /transponder 路径前缀。
        // 旧实现清空 Path 会得到 wss://host + wsPath，若 wsPath 不含
        // /transponder 会连错（Python 客户端是 host+/transponder+wsPath）。
        if (!Uri.TryCreate(httpBaseUrl, UriKind.Absolute, out var httpUri))
        {
            throw new InvalidOperationException(
                $"无效的 BaseUrl: {httpBaseUrl}");
        }

        var scheme = httpUri.Scheme.Equals(
                         "https",
                         StringComparison.OrdinalIgnoreCase)
            ? "wss"
            : "ws";

        if (!wsPath.StartsWith('/'))
            wsPath = "/" + wsPath;

        var basePath = httpUri.AbsolutePath.TrimEnd('/');
        string fullPath;
        if (string.IsNullOrEmpty(basePath) || basePath == "/")
        {
            fullPath = wsPath;
        }
        else if (wsPath.StartsWith(
                     basePath + "/",
                     StringComparison.OrdinalIgnoreCase) ||
                 wsPath.Equals(
                     basePath,
                     StringComparison.OrdinalIgnoreCase))
        {
            fullPath = wsPath;
        }
        else
        {
            fullPath = basePath + wsPath;
        }

        return $"{scheme}://{httpUri.Authority}{fullPath}";
    }

    private static void EnsureApiOk(JsonElement root, string action)
    {
        if (TryGetStatusOk(root))
            return;

        string? name = null;
        string? detail = null;
        if (root.TryGetProperty("status_name", out var n) &&
            n.ValueKind == JsonValueKind.String)
        {
            name = n.GetString();
        }

        if (root.TryGetProperty("status_detail", out var d) &&
            d.ValueKind == JsonValueKind.String)
        {
            detail = d.GetString();
        }

        var statusText = "?";
        if (root.TryGetProperty("status", out var st))
            statusText = st.ToString();

        throw new InvalidOperationException(
            $"{action}失败 (status={statusText}): " +
            $"{name ?? "unknown"} / {detail ?? "no detail"}");
    }

    private static bool TryGetStatusOk(JsonElement root)
    {
        if (!root.TryGetProperty("status", out var status))
            return false;

        if (status.ValueKind == JsonValueKind.Number &&
            status.TryGetInt32(out var n))
        {
            return n == 1;
        }

        if (status.ValueKind == JsonValueKind.String &&
            int.TryParse(status.GetString(), out n))
        {
            return n == 1;
        }

        return false;
    }

    private void SetState(DataSourceState state)
    {
        if (_state == state)
            return;

        _state = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            _loopCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            _loopCts?.Dispose();
        }
        catch
        {
            // ignore
        }

        _loopCts = null;

        if (_state is DataSourceState.Connected
            or DataSourceState.Connecting
            or DataSourceState.Reconnecting)
        {
            SetState(DataSourceState.Disconnected);
        }

        Interlocked.Exchange(ref _started, 0);
    }
}
