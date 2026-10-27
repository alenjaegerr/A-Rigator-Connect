using System.Net.Http.Json;
using System.Text.Json;
using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public sealed class RobotHttpCommunicationService : IRobotCommunicationService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    public RobotHttpCommunicationService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(RobotNetworkOptions.BaseAddress),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default)
    {
        var endpoint = command switch
        {
            RobotControlCommand.Forward => "api/control/forward",
            RobotControlCommand.Reverse => "api/control/reverse",
            RobotControlCommand.Stop => "api/control/stop",
            RobotControlCommand.PumpOn => "api/control/pump/on",
            RobotControlCommand.PumpOff => "api/control/pump/off",
            RobotControlCommand.ResetShield => "api/control/shield/reset",
            RobotControlCommand.Heartbeat => "api/control/heartbeat",
            RobotControlCommand.StartAutonomous => "api/control/autonomous/start",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported robot command.")
        };

        try
        {
            using var response = await _httpClient.PostAsync(endpoint, content: null, cancellationToken);
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
    }

    public ConnectionState ConnectionState => _connectionState;

    public event Action<ConnectionState>? ConnectionStateChanged;

    public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(RobotNetworkOptions.StatusEndpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var status = await response.Content.ReadFromJsonAsync<RobotStatusResponse>(JsonOptions, cancellationToken);
            return status is { Success: true };
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
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
        using var response = await _httpClient.GetAsync(RobotNetworkOptions.StatusEndpoint, cancellationToken);
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
            PumpIsOn = statusResponse.PumpIsOn ?? false,
            ControlsEnabled = statusResponse.ControlsEnabled
        };
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
