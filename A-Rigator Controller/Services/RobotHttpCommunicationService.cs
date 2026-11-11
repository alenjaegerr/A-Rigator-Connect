using System.Net.Http.Json;
using System.Text.Json;
using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public sealed class RobotHttpCommunicationService : IRobotCommunicationService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private Uri _baseAddress = new("http://arigator.local/");
    private Uri? _fallbackBaseAddress;
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    public RobotHttpCommunicationService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default)
    {
        var endpoint = command switch
        {
            RobotControlCommand.Forward => "api/control/forward",
            RobotControlCommand.Reverse => "api/control/reverse",
            RobotControlCommand.SteerLeft => "api/control/steer/left",
            RobotControlCommand.SteerRight => "api/control/steer/right",
            RobotControlCommand.SteerCenter => "api/control/steer/center",
            RobotControlCommand.NozzleSweep => "api/control/nozzle/sweep",
            RobotControlCommand.Stop => "api/control/stop",
            RobotControlCommand.PumpOn => "api/control/pump/on",
            RobotControlCommand.PumpOff => "api/control/pump/off",
            RobotControlCommand.ResetShield => "api/control/shield/reset",
            RobotControlCommand.Heartbeat => "api/control/heartbeat",
            RobotControlCommand.AutonomousStart => "api/control/autonomous/start",
            RobotControlCommand.AutonomousStop => "api/control/autonomous/stop",
            RobotControlCommand.PathwayStart => "api/control/pathway/start",
            RobotControlCommand.PathwayStop => "api/control/pathway/stop",
            RobotControlCommand.TerrainStart => "api/control/terrain/start",
            RobotControlCommand.TerrainBiasLeft => "api/control/terrain/bias/left",
            RobotControlCommand.TerrainBiasRight => "api/control/terrain/bias/right",
            RobotControlCommand.TerrainBiasCenter => "api/control/terrain/bias/center",
            RobotControlCommand.TerrainStop => "api/control/terrain/stop",
            RobotControlCommand.TerrainStart => "api/control/terrain/start",
            RobotControlCommand.TerrainBiasLeft => "api/control/terrain/bias/left",
            RobotControlCommand.TerrainBiasRight => "api/control/terrain/bias/right",
            RobotControlCommand.TerrainBiasCenter => "api/control/terrain/bias/center",
            RobotControlCommand.TerrainStop => "api/control/terrain/stop",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported robot command.")
        };

        try
        {
            using var response = await _httpClient.PostAsync(new Uri(_baseAddress, endpoint), content: null, cancellationToken);
            response.EnsureSuccessStatusCode();

            var commandResponse = await response.Content.ReadFromJsonAsync<RobotCommandResponse>(JsonOptions, cancellationToken);
            if (commandResponse is not { Success: true })
            {
                throw new InvalidOperationException("The robot rejected the command.");
            }
        }
        catch (HttpRequestException)
        {
            SetConnectionState(ConnectionState.Disconnected);
            throw;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetConnectionState(ConnectionState.Disconnected);
            throw;
        }
        catch (JsonException)
        {
            SetConnectionState(ConnectionState.Disconnected);
            throw;
        }
    }

    public async Task ApplyIrrigationProfileAsync(IrrigationProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var query = $"left={profile.LeftDurationSeconds}&back={profile.BackDurationSeconds}&right={profile.RightDurationSeconds}&repeat={profile.RepeatCount}";
        using var response = await _httpClient.PostAsync(
            new Uri(_baseAddress, $"api/irrigation/profile?{query}"),
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RobotCommandResponse>(JsonOptions, cancellationToken);
        if (result is not { Success: true })
            throw new InvalidOperationException("A-Rigator rejected the irrigation profile.");
    }

    public void SetRobotAddress(string address)
    {
        if (!Uri.TryCreate($"http://{address}/", UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("The robot returned an invalid network address.", nameof(address));

        if (uri.Host == "192.168.4.1")
        {
            // Prefer the direct AP for this attempt, but retain the last known
            // station IP so IoT discovery remains available as a fallback.
            _baseAddress = uri;
            ActiveConnectionMode = RobotConnectionMode.AccessPoint;
            return;
        }

        _fallbackBaseAddress = uri;
        _baseAddress = new Uri("http://arigator.local/");
    }

    public ConnectionState ConnectionState => _connectionState;

    public RobotConnectionMode ActiveConnectionMode { get; private set; } = RobotConnectionMode.Unknown;

    public event Action<ConnectionState>? ConnectionStateChanged;

    public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<(Uri Address, RobotConnectionMode Mode)>
        {
            (_baseAddress, _baseAddress.Host == "192.168.4.1"
                ? RobotConnectionMode.AccessPoint
                : RobotConnectionMode.IoT)
        };

        if (_fallbackBaseAddress is not null &&
            !_fallbackBaseAddress.Equals(_baseAddress))
        {
            candidates.Add((_fallbackBaseAddress, RobotConnectionMode.IoT));
        }

        var accessPointAddress = new Uri(RobotNetworkOptions.BaseAddress);
        if (!candidates.Any(candidate => candidate.Address.Equals(accessPointAddress)))
        {
            candidates.Add((accessPointAddress, RobotConnectionMode.AccessPoint));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    new Uri(candidate.Address, RobotNetworkOptions.StatusEndpoint),
                    cancellationToken);
                if (!response.IsSuccessStatusCode) continue;

                var status = await response.Content.ReadFromJsonAsync<RobotStatusResponse>(JsonOptions, cancellationToken);
                if (status is { Success: true })
                {
                    _baseAddress = candidate.Address;
                    ActiveConnectionMode = candidate.Mode;
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Try the BLE-provided station IP, then the robot's setup AP.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // mDNS can time out on Android; continue to the next candidate.
            }
            catch (JsonException)
            {
                // Continue to the next known address.
            }
        }

        return false;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        SetConnectionState(ConnectionState.Connecting);
        if (await CheckAvailabilityAsync(cancellationToken))
        {
            SetConnectionState(ConnectionState.Connected);
            return;
        }

        SetConnectionState(ConnectionState.Disconnected);
        throw new InvalidOperationException("A-Rigator did not return a valid status response.");
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetConnectionState(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public async Task<RobotStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                new Uri(_baseAddress, RobotNetworkOptions.StatusEndpoint),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var statusResponse = await response.Content.ReadFromJsonAsync<RobotStatusResponse>(JsonOptions, cancellationToken);
            if (statusResponse is not { Success: true })
            {
                return null;
            }

            var state = Enum.TryParse<RobotState>(statusResponse.State, true, out var parsedState)
                ? parsedState
                : RobotState.Idle;
            var mode = Enum.TryParse<OperatingMode>(statusResponse.Mode, true, out var parsedMode)
                ? parsedMode
                : (OperatingMode?)null;

            return new RobotStatus
            {
                State = state,
                Mode = mode,
                AutonomousMode = statusResponse.AutonomousMode,
                PumpIsOn = statusResponse.PumpIsOn ?? false,
                ControlsEnabled = statusResponse.ControlsEnabled,
                WifiConnected = statusResponse.WifiConnected,
                WifiIp = statusResponse.WifiIp,
                Hostname = statusResponse.Hostname,
                ConnectionMode = ActiveConnectionMode,
                ObstacleDistanceCm = statusResponse.ObstacleDistanceCm,
                ObstacleEchoDetected = statusResponse.ObstacleEchoDetected,
                ObstacleDetected = statusResponse.ObstacleDetected,
                ObstacleStopThresholdCm = statusResponse.ObstacleStopThresholdCm,
                ManualControlTimeoutMs = statusResponse.ManualControlTimeoutMs,
                LineGpio14 = statusResponse.LineGpio14,
                LineGpio27 = statusResponse.LineGpio27
            };
        }
        catch (HttpRequestException)
        {
            SetConnectionState(ConnectionState.Disconnected);
            throw;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetConnectionState(ConnectionState.Disconnected);
            throw;
        }
        catch (JsonException)
        {
            SetConnectionState(ConnectionState.Disconnected);
            throw;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private void SetConnectionState(ConnectionState state)
    {
        if (_connectionState == state)
        {
            return;
        }

        _connectionState = state;
        ConnectionStateChanged?.Invoke(state);
    }
}
