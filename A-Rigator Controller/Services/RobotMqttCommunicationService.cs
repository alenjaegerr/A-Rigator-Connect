using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
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
                .WithTlsOptions(tls => tls
                    .UseTls()
                    // Keep SNI and hostname validation bound to the provisioned broker host.
                    // Mozilla roots are consulted only if platform validation reports
                    // a chain-only error; hostname mismatches are always rejected.
                    .WithTargetHost(_options.BrokerHost)
                    .WithCertificateValidationHandler(MozillaRootCertificateValidator.Validate))
                .WithCleanSession()
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .Build();

            await _client.ConnectAsync(clientOptions, cancellationToken);
            await _client.SubscribeAsync(_ackTopic, MqttQualityOfServiceLevel.AtLeastOnce, cancellationToken);
            await _client.SubscribeAsync(_telemetryTopic, MqttQualityOfServiceLevel.AtLeastOnce, cancellationToken);
            SetConnectionState(ConnectionState.Connected);
        }
        catch (Exception ex)
        {
            // Preserve the native Android TLS exception details. The platform
            // SslException can have an empty Message, hiding the actual cause.
            System.Diagnostics.Debug.WriteLine(
                $"[MQTT TLS] Connect failed: {ex}");
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

    public Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default)
    {
        var commandName = command switch
        {
            RobotControlCommand.Forward => "forward",
            RobotControlCommand.Reverse => "reverse",
            RobotControlCommand.SteerLeft => "steer_left",
            RobotControlCommand.SteerRight => "steer_right",
            RobotControlCommand.SteerCenter => "steer_center",
            RobotControlCommand.NozzleSweep => "nozzle_sweep",
            RobotControlCommand.NozzleSweepLeft => "nozzle_sweep_left",
            RobotControlCommand.NozzleSweepBack => "nozzle_sweep_back",
            RobotControlCommand.NozzleSweepRight => "nozzle_sweep_right",
            RobotControlCommand.NozzleSweepFull => "nozzle_sweep_full",
            RobotControlCommand.NozzleSweepStop => "nozzle_sweep_stop",
            RobotControlCommand.ManualIrrigationStart => "irrigation_start",
            RobotControlCommand.ManualIrrigationStop => "irrigation_stop",
            RobotControlCommand.Stop => "stop",
            RobotControlCommand.PumpOn => "pump_on",
            RobotControlCommand.PumpOff => "pump_off",
            RobotControlCommand.ResetShield => "reset_shield",
            RobotControlCommand.FactoryReset => "factory_reset",
            RobotControlCommand.Heartbeat => "heartbeat",
            RobotControlCommand.AutonomousStart => "autonomous_start",
            RobotControlCommand.AutonomousStop => "autonomous_stop",
            RobotControlCommand.PathwayStart => "pathway_start",
            RobotControlCommand.PathwayStop => "pathway_stop",
            RobotControlCommand.TerrainStart => "terrain_start",
            RobotControlCommand.TerrainBiasLeft => "terrain_bias_left",
            RobotControlCommand.TerrainBiasRight => "terrain_bias_right",
            RobotControlCommand.TerrainBiasCenter => "terrain_bias_center",
            RobotControlCommand.TerrainStop => "terrain_stop",
            RobotControlCommand.ManualTerrainStart => "manual_terrain_start",
            RobotControlCommand.ManualTerrainStop => "manual_terrain_stop",
            _ => throw new NotSupportedException($"'{command}' has no MQTT wire mapping.")
        };
        return PublishCommandAsync(commandName, new { }, command, cancellationToken);
    }

    public Task ApplyIrrigationProfileAsync(IrrigationProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.ApplyDirectionMode();
        return PublishCommandAsync("irrigation_profile", new
        {
            left = profile.LeftDurationSeconds,
            back = profile.BackDurationSeconds,
            right = profile.RightDurationSeconds,
            repeat = profile.RepeatCount
        }, null, cancellationToken);
    }

    public Task ApplyHardwareConfigurationAsync(RobotHardwareConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        return PublishCommandAsync("robot_config", new
        {
            steeringLeft = configuration.SteeringLeftAngle, steeringCenter = configuration.SteeringCenterAngle,
            steeringRight = configuration.SteeringRightAngle, nozzleMinimum = configuration.NozzleMinimumAngle,
            nozzleCenter = configuration.NozzleCenterAngle, nozzleMaximum = configuration.NozzleMaximumAngle,
            nozzleLeftSectorMaximum = configuration.NozzleLeftSectorMaximumAngle,
            nozzleRightSectorMinimum = configuration.NozzleRightSectorMinimumAngle
        }, null, cancellationToken);
    }

    public Task StartManualIrrigationAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(RobotControlCommand.ManualIrrigationStart, cancellationToken);

    public Task StopManualIrrigationAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(RobotControlCommand.ManualIrrigationStop, cancellationToken);

    private async Task PublishCommandAsync(string commandName, object payload, RobotControlCommand? command,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);
        var commandId = Guid.NewGuid().ToString("N");
        var acknowledgement = new TaskCompletionSource<MqttCommandAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingAcks.TryAdd(commandId, acknowledgement))
            throw new InvalidOperationException("Could not register the remote command acknowledgement.");
        try
        {
            var ttlMs = command is RobotControlCommand.Stop or RobotControlCommand.PumpOff
                or RobotControlCommand.AutonomousStop or RobotControlCommand.PathwayStop
                or RobotControlCommand.TerrainStop or RobotControlCommand.ManualIrrigationStop
                or RobotControlCommand.NozzleSweepStop or RobotControlCommand.ManualTerrainStop
                ? 5000 : 1500;
            var envelope = new
            {
                version = 1,
                commandId,
                command = commandName,
                issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ttlMs,
                payload
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
            if (!string.Equals(ack.Status, "executed", StringComparison.OrdinalIgnoreCase) &&
                !(command == RobotControlCommand.FactoryReset && string.Equals(ack.Status, "accepted", StringComparison.OrdinalIgnoreCase)))
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
                // Preserve the robot-reported operating mode and connection state.
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
