using System.IO;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Communication.Remote;

/// <summary>
/// CMTS remote Android data source (protocol v0.1).
/// WebSocket client: hello → subscribe → samples into <see cref="DataBus"/>.
/// Does not modify GSpot protocol or parsers.
/// </summary>
public sealed class CMTSRemoteDataSource : IDataSource
{
    private readonly DataBus _dataBus;
    private readonly CMTSRemoteOptions _options;

    private long _received;
    private long _valid;
    private long _lost;
    private long _outOfOrder;
    private long? _lastSeq;

    private DataSourceState _state = DataSourceState.Disconnected;
    private int _started;
    private int _disposed;

    private CancellationTokenSource? _loopCts;
    private Task? _runTask;

    private readonly object _sendGate = new();
    private ClientWebSocket? _ws;
    private string? _deviceId;
    private string? _deviceLabel;
    private string _recordState = "IDLE";
    private string? _lastError;

    private readonly object _statusGate = new();
    private CMTSRemoteParser.DeviceStatus? _lastDeviceStatus;
    private readonly HashSet<string> _seenChannelIds = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<CMTSRemoteParser.Envelope>> _pending =
        new(StringComparer.Ordinal);

    public CMTSRemoteDataSource(DataBus dataBus, CMTSRemoteOptions options)
    {
        _dataBus = dataBus ?? throw new ArgumentNullException(nameof(dataBus));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "CMTS-Remote";

    public DataSourceState State => _state;

    public DataSourceStats Stats => new(
        Interlocked.Read(ref _received),
        Interlocked.Read(ref _valid),
        Interlocked.Read(ref _lost),
        Interlocked.Read(ref _outOfOrder));

    public event EventHandler<DataSourceState>? StateChanged;

    public event EventHandler<string>? RecordStateChanged;

    public string? LastError => _lastError;

    public string? DeviceId => _deviceId;

    public string? DeviceLabel => _deviceLabel;

    public string RecordState => _recordState;

    public string Endpoint => _options.BuildWebSocketUrl();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            return Task.CompletedTask;

        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;
        SetState(DataSourceState.Connecting);
        _runTask = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = _loopCts;
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { /* ignore */ }
        }

        var runTask = _runTask;
        if (runTask is not null)
        {
            try { await runTask.ConfigureAwait(false); } catch { /* ignore */ }
        }

        Dispose();
    }

    public Task<CMTSRemoteParser.RecordStatus> StartRecordingAsync(
        CancellationToken cancellationToken = default) =>
        SendRecordCommandAsync("record.start", cancellationToken);

    public Task<CMTSRemoteParser.RecordStatus> StopRecordingAsync(
        CancellationToken cancellationToken = default) =>
        SendRecordCommandAsync("record.stop", cancellationToken);

    private async Task<CMTSRemoteParser.RecordStatus> SendRecordCommandAsync(
        string type,
        CancellationToken cancellationToken)
    {
        if (_state != DataSourceState.Connected || _ws is null)
        {
            throw new InvalidOperationException("Not connected to remote device.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var payload = new Dictionary<string, object?>
        {
            ["mode"] = "manual",
            ["requestId"] = requestId
        };

        var json = CMTSRemoteParser.BuildEnvelopeJson(
            type,
            requestId,
            payload,
            _deviceId);

        var tcs = new TaskCompletionSource<CMTSRemoteParser.Envelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        try
        {
            await SendTextAsync(json, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.CommandTimeoutMs);
            await using var reg = timeoutCts.Token.Register(
                () => tcs.TrySetCanceled(timeoutCts.Token));

            var env = await tcs.Task.ConfigureAwait(false);
            var status = CMTSRemoteParser.ParseRecordStatus(env.Payload);
            UpdateRecordState(status.State);
            return status;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var delayMs = _options.InitialReconnectDelayMs;
        var attempt = 0;

        while (!ct.IsCancellationRequested && _disposed == 0)
        {
            try
            {
                SetState(attempt == 0
                    ? DataSourceState.Connecting
                    : DataSourceState.Reconnecting);

                await ConnectSessionAsync(ct).ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested || _disposed != 0)
                    break;

                _lastError = ex.Message;
                FailPending(ex);
                SetState(DataSourceState.Reconnecting);
                attempt++;

                try
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                delayMs = Math.Min(delayMs * 2, _options.MaxReconnectDelayMs);
            }
            finally
            {
                await CloseSocketAsync().ConfigureAwait(false);
            }
        }

        FailPending(new OperationCanceledException("Remote session ended."));

        if (_disposed != 0 || ct.IsCancellationRequested)
            SetState(DataSourceState.Disconnected);
        else
            SetState(DataSourceState.Faulted);

        Interlocked.Exchange(ref _started, 0);
    }

    private async Task ConnectSessionAsync(CancellationToken ct)
    {
        var url = _options.BuildWebSocketUrl();
        var ws = new ClientWebSocket();
        _ws = ws;

        await ws.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);

        var helloId = Guid.NewGuid().ToString("N");
        var helloPayload = new Dictionary<string, object?>
        {
            ["client"] = "cmts-win",
            ["clientVer"] = _options.ClientVer,
            ["token"] = _options.Token ?? "",
            ["protoMin"] = CMTSRemoteParser.ProtocolVersion,
            ["protoMax"] = CMTSRemoteParser.ProtocolVersion
        };

        var helloJson = CMTSRemoteParser.BuildEnvelopeJson(
            "device.hello",
            helloId,
            helloPayload);

        var helloTcs = new TaskCompletionSource<CMTSRemoteParser.Envelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[helloId] = helloTcs;

        try
        {
            await SendTextAsync(helloJson, ct).ConfigureAwait(false);

            // Read until hello ack (control before subscribe).
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.CommandTimeoutMs);
            var helloCt = timeoutCts.Token;

            CMTSRemoteParser.Envelope helloEnv;
            try
            {
                while (!helloTcs.Task.IsCompleted)
                {
                    helloCt.ThrowIfCancellationRequested();
                    var msg = await ReceiveOneMessageAsync(helloCt).ConfigureAwait(false);
                    if (msg is null)
                    {
                        throw new IOException(
                            "Remote WebSocket closed during device.hello.");
                    }

                    HandleIncoming(msg);
                }

                helloEnv = await helloTcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    "device.hello timed out — is Android WS listening on " + url + "?");
            }
            var ack = CMTSRemoteParser.ParseHelloAck(helloEnv);
            if (!ack.Ok)
            {
                var code = ack.ErrorCode ?? "auth_failed";
                throw new InvalidOperationException(
                    $"device.hello failed: {code}");
            }

            _deviceId = ack.DeviceId ?? helloEnv.DeviceId;
            _deviceLabel = ack.DeviceLabel;
            if (!string.IsNullOrWhiteSpace(ack.RecordState))
                UpdateRecordState(ack.RecordState!);

            _lastError = null;
            SetState(DataSourceState.Connected);

            var subId = Guid.NewGuid().ToString("N");
            var subPayload = new Dictionary<string, object?>
            {
                ["channels"] = new[] { "core" },
                ["maxHz"] = _options.MaxHz
            };
            var subJson = CMTSRemoteParser.BuildEnvelopeJson(
                "stream.subscribe",
                subId,
                subPayload,
                _deviceId);
            await SendTextAsync(subJson, ct).ConfigureAwait(false);

            await ReceiveLoopAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(helloId, out _);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested &&
               _ws is { State: WebSocketState.Open })
        {
            var msg = await ReceiveOneMessageAsync(ct).ConfigureAwait(false);
            if (msg is null)
            {
                throw new IOException("Remote WebSocket closed.");
            }

            HandleIncoming(msg);
        }

        if (!ct.IsCancellationRequested)
            throw new IOException("Remote WebSocket disconnected.");
    }

    private async Task<string?> ReceiveOneMessageAsync(CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null)
            return null;

        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                try
                {
                    await ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "closed",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }

                return null;
            }

            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
    }

    private void HandleIncoming(string json)
    {
        Interlocked.Increment(ref _received);

        if (!CMTSRemoteParser.TryParseEnvelope(json, out var env) || env is null)
            return;

        // Complete pending control requests first (control priority).
        if (!string.IsNullOrWhiteSpace(env.Id) &&
            _pending.TryRemove(env.Id!, out var tcs))
        {
            tcs.TrySetResult(env);
        }

        switch (env.Type)
        {
            case "device.hello":
                // Already handled via pending if id matches; ignore stray.
                break;

            case "stream.sample":
                HandleSample(env);
                break;

            case "record.status":
                HandleRecordStatus(env);
                break;

            case "device.status":
                HandleDeviceStatus(env);
                break;
        }
    }

    private void HandleSample(CMTSRemoteParser.Envelope env)
    {
        var mapped = CMTSRemoteParser.TryMapSample(env, _deviceId);
        if (mapped.Sample is null)
            return;

        TrackSequence(mapped.Seq);
        Interlocked.Increment(ref _valid);

        if (!string.IsNullOrWhiteSpace(mapped.Sample.VehicleId))
            _deviceId = mapped.Sample.VehicleId;
        if (!string.IsNullOrWhiteSpace(mapped.Sample.VehicleLabel))
            _deviceLabel = mapped.Sample.VehicleLabel;

        // Ride slow status fields (sats…) on the live sample Channels dict.
        // TryMapSample always supplies a mutable Dictionary under IReadOnlyDictionary.
        CMTSRemoteParser.DeviceStatus? status;
        lock (_statusGate)
            status = _lastDeviceStatus;

        if (status is not null &&
            mapped.Sample.Channels is Dictionary<string, double> dict)
        {
            CMTSRemoteParser.MergeStatusChannels(dict, status);
        }

        RegisterSeenChannels(mapped.Sample.Channels.Keys);

        _dataBus.TryPublish(mapped.Sample);
    }

    private void HandleDeviceStatus(CMTSRemoteParser.Envelope env)
    {
        if (env.Payload.ValueKind != JsonValueKind.Object)
        {
            if (!string.IsNullOrWhiteSpace(env.DeviceId))
                _deviceId = env.DeviceId;
            return;
        }

        var status = CMTSRemoteParser.ParseDeviceStatus(env.Payload);
        if (!string.IsNullOrWhiteSpace(status.RecordState))
            UpdateRecordState(status.RecordState!);

        lock (_statusGate)
            _lastDeviceStatus = status;

        RegisterSeenChannels(status.Channels.Keys);

        if (!string.IsNullOrWhiteSpace(env.DeviceId))
            _deviceId = env.DeviceId;
    }

    private void RegisterSeenChannels(IEnumerable<string>? keys)
    {
        if (keys is null)
            return;

        List<string>? fresh = null;
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (_seenChannelIds.Add(key))
                (fresh ??= new List<string>()).Add(key);
        }

        if (fresh is { Count: > 0 })
            ChannelRegistry.Instance.MergeRemoteKeys(fresh);
    }

    private void HandleRecordStatus(CMTSRemoteParser.Envelope env)
    {
        var status = CMTSRemoteParser.ParseRecordStatus(env.Payload);
        UpdateRecordState(status.State);

        if (!string.IsNullOrWhiteSpace(env.Id) &&
            _pending.TryGetValue(env.Id!, out var tcs))
        {
            // Idempotent status may arrive without removing if already completed.
            tcs.TrySetResult(env);
        }
    }

    private void TrackSequence(ulong seq)
    {
        var asLong = unchecked((long)seq);
        var prev = _lastSeq;
        _lastSeq = asLong;
        if (prev is null)
            return;

        if (asLong > prev.Value + 1)
            Interlocked.Add(ref _lost, asLong - prev.Value - 1);
        else if (asLong < prev.Value)
            Interlocked.Increment(ref _outOfOrder);
    }

    private void UpdateRecordState(string state)
    {
        if (string.Equals(_recordState, state, StringComparison.Ordinal))
            return;

        _recordState = state;
        RecordStateChanged?.Invoke(this, state);
    }

    private async Task SendTextAsync(string json, CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null || ws.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not open.");

        var bytes = Encoding.UTF8.GetBytes(json);
        // Serialize sends — control frames must not race sample ACKs.
        var lockTaken = false;
        try
        {
            Monitor.Enter(_sendGate, ref lockTaken);
            await ws.SendAsync(
                    bytes,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
                Monitor.Exit(_sendGate);
        }
    }

    private async Task CloseSocketAsync()
    {
        var ws = Interlocked.Exchange(ref _ws, null);
        if (ws is null)
            return;

        try
        {
            if (ws.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "bye",
                        cts.Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            try { ws.Dispose(); } catch { /* ignore */ }
        }
    }

    private void FailPending(Exception ex)
    {
        foreach (var kv in _pending)
        {
            if (_pending.TryRemove(kv.Key, out var tcs))
                tcs.TrySetException(ex);
        }
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

        try { _loopCts?.Cancel(); } catch { /* ignore */ }
        try { _loopCts?.Dispose(); } catch { /* ignore */ }
        _loopCts = null;

        try
        {
            _ws?.Dispose();
        }
        catch
        {
            // ignore
        }

        _ws = null;

        if (_state is DataSourceState.Connected
            or DataSourceState.Connecting
            or DataSourceState.Reconnecting)
        {
            SetState(DataSourceState.Disconnected);
        }

        Interlocked.Exchange(ref _started, 0);
    }
}
