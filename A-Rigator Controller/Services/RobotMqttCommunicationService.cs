using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using A_Rigator_Controller.Models;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace A_Rigator_Controller.Services;

/// <summary>
/// Cloud MQTT transport. It publishes command envelopes and waits for the
/// matching ESP32 acknowledgement. Broker connectivity and robot reachability
/// are exposed separately so the UI never mistakes a broker session for an
/// online robot.
/// </summary>
public sealed class RobotMqttCommunicationService : IRobotTransport, IRemoteRobotHealth, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IRemoteRobotConfigurationStore _configurationStore;
    private readonly IMqttClient _client;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<MqttCommandAck>> _pendingAcks = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private RemoteRobotConnectionOptions? _options;
    private RobotStatus? _latestStatus;
    private DateTimeOffset? _lastTelemetryAt;
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    // MQTT can be connected while the ESP32 is offline. Treat the robot as
    // reachable only while fresh telemetry has been received.
    private static readonly TimeSpan TelemetryFreshnessWindow = TimeSpan.FromSeconds(20);

    public DateTimeOffset? LastTelemetryAt => _lastTelemetryAt;

    public bool IsBrokerConnected => _client.IsConnected;

    public bool IsRobotResponsive =>
        _client.IsConnected &&
        _lastTelemetryAt is { } receivedAt &&
        DateTimeOffset.UtcNow - receivedAt <= TelemetryFreshnessWindow;

    public event Action<bool>? RobotReachabilityChanged;
    public event Action? HealthChanged;
    private bool _lastReportedReachability;
    private string? _ackTopic;
    private string? _telemetryTopic;

    public RobotMqttCommunicationService(IRemoteRobotConfigurationStore configurationStore)
    {
        _configurationStore = configurationStore;
        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += HandleMessageAsync;
        _client.DisconnectedAsync += HandleDisconnectedAsync;
    }

    public RobotTransportKind TransportKind => RobotTransportKind.Remote;
    public ConnectionState ConnectionState => _connectionState;
    public event Action<ConnectionState>? ConnectionStateChanged;

    public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        _options = await _configurationStore.LoadAsync(cancellationToken);
        return _options is not null;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_client.IsConnected)
            {
                SetConnectionState(ConnectionState.Connected);
                return;
            }

            _options = await _configurationStore.LoadAsync(cancellationToken);
            if (_options is null)
                throw new InvalidOperationException("Remote access has not been provisioned for this robot.");

            SetConnectionState(ConnectionState.Connecting);
            _ackTopic = Topic("ack");
            _telemetryTopic = Topic("telemetry");

            var clientOptions = new MqttClientOptionsBuilder()
                .WithClientId($"arigator-app-{Guid.NewGuid():N}")
                .WithTcpServer(_options.BrokerHost, _options.BrokerPort)
                .WithCredentials(_options.Username, _options.Password)
                .WithTlsOptions(tls => tls.UseTls())
                .WithCleanSession()
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .Build();

            await _client.ConnectAsync(clientOptions, cancellationToken);
            await _client.SubscribeAsync(_ackTopic, MqttQualityOfServiceLevel.AtLeastOnce, cancellationToken);
            await _client.SubscribeAsync(_telemetryTopic, MqttQualityOfServiceLevel.AtLeastOnce, cancellationToken);
            SetConnectionState(ConnectionState.Connected);
        }
        catch
        {
            SetConnectionState(ConnectionState.Disconnected);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client.IsConnected)
            await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cancellationToken);

        foreach (var pending in _pendingAcks.Values)
            pending.TrySetCanceled(cancellationToken);
        _pendingAcks.Clear();
        SetConnectionState(ConnectionState.Disconnected);
    }

    public Task<RobotStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportRobotReachability();
        return Task.FromResult(IsRobotResponsive ? _latestStatus : null);
    }

    public async Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default)
    {
        var commandName = command switch
        {
            RobotControlCommand.Forward => "forward",
            RobotControlCommand.Reverse => "reverse",
            RobotControlCommand.Stop => "stop",
            RobotControlCommand.PumpOn => "pump_on",
            RobotControlCommand.PumpOff => "pump_off",
            RobotControlCommand.Heartbeat => "heartbeat",
            RobotControlCommand.AutonomousStart => "autonomous_start",
            RobotControlCommand.AutonomousStop => "autonomous_stop",
            _ => throw new NotSupportedException(
                $"'{command}' is not enabled for remote control yet. Use the local connection or wait for the next firmware command rollout.")
        };

        await EnsureConnectedAsync(cancellationToken);

        var commandId = Guid.NewGuid().ToString("N");
        var acknowledgement = new TaskCompletionSource<MqttCommandAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingAcks.TryAdd(commandId, acknowledgement))
            throw new InvalidOperationException("Could not register the remote command acknowledgement.");

        try
        {
            var ttlMs = command is RobotControlCommand.Stop
                or RobotControlCommand.PumpOff
                or RobotControlCommand.AutonomousStop
                ? 5000
                : 1500;
            var envelope = new
            {
                version = 1,
                commandId,
                                command = commandName,
                issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ttlMs,
                payload = new { }
            };
            var json = JsonSerializer.Serialize(envelope, JsonOptions);
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(Topic("command"))
                .WithPayload(json)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client.PublishAsync(message, cancellationToken);
            var completed = await Task.WhenAny(acknowledgement.Task, Task.Delay(TimeSpan.FromSeconds(4), cancellationToken));
            if (completed != acknowledgement.Task)
                throw new TimeoutException("The robot did not acknowledge the remote command. Check its internet connection and try again.");

            var ack = await acknowledgement.Task;
            if (!string.Equals(ack.Status, "executed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(ack.Message ?? $"The robot rejected the command ({ack.Code ?? ack.Status}).");
        }
        catch (Exception) when (!_client.IsConnected)
        {
            SetConnectionState(ConnectionState.Disconnected);
            throw;
        }
        finally
        {
            _pendingAcks.TryRemove(commandId, out _);
        }
    }

    public Task ApplyIrrigationProfileAsync(IrrigationProfile profile, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Irrigation profile commands are not enabled over MQTT yet.");

    public Task StartManualIrrigationAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Manual irrigation commands are not enabled over MQTT yet.");

    public Task StopManualIrrigationAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Manual irrigation commands are not enabled over MQTT yet.");

    public void SetRobotAddress(string address)
    {
        // Remote transport is addressed by the provisioned robot ID, not LAN IP.
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected) return;
        await ConnectAsync(cancellationToken);
        if (!_client.IsConnected)
            throw new InvalidOperationException("The remote MQTT connection could not be established.");
    }

    private static string Topic(string leaf) => $"arigator/v1/{leaf}";

    private Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment.AsSpan());

        if (topic == _ackTopic)
        {
            try
            {
                var ack = JsonSerializer.Deserialize<MqttCommandAck>(payload, JsonOptions);
                if (ack is not null && !string.IsNullOrWhiteSpace(ack.CommandId) &&
                    _pendingAcks.TryGetValue(ack.CommandId, out var completion))
                    completion.TrySetResult(ack);
            }
            catch (JsonException) { }
        }
        else if (topic == _telemetryTopic)
        {
            try
            {
                var status = JsonSerializer.Deserialize<RobotStatus>(payload, JsonOptions);
                if (status is not null)
                {
                    _latestStatus = status;
                    _lastTelemetryAt = DateTimeOffset.UtcNow;
                    ReportRobotReachability();
                    HealthChanged?.Invoke();
                }
            }
            catch (JsonException) { }
        }

        return Task.CompletedTask;
    }

    private Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        SetConnectionState(ConnectionState.Disconnected);
        ReportRobotReachability();
        foreach (var pending in _pendingAcks.Values)
            pending.TrySetException(new IOException("The MQTT connection was lost before the robot acknowledged the command."));
        return Task.CompletedTask;
    }

    private void SetConnectionState(ConnectionState state)
    {
        if (_connectionState == state) return;
        _connectionState = state;
        ConnectionStateChanged?.Invoke(state);
        ReportRobotReachability();
        HealthChanged?.Invoke();
    }

    private void ReportRobotReachability()
    {
        var reachable = IsRobotResponsive;
        if (_lastReportedReachability == reachable) return;
        _lastReportedReachability = reachable;
        RobotReachabilityChanged?.Invoke(reachable);
        HealthChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build());
        }
        catch { }
        _client.Dispose();
        _connectionLock.Dispose();
    }

    private sealed class MqttCommandAck
    {
        public string? CommandId { get; set; }
        public string? Status { get; set; }
        public string? Code { get; set; }
        public string? Message { get; set; }
    }
}
