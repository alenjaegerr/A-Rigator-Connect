using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

/// <summary>
/// Coordinates local and remote communication paths. Local transports are
/// preferred when reachable; remote transports are attempted when local
/// connectivity is unavailable.
/// </summary>
public sealed class RobotConnectionManager : IRobotCommunicationService
{
    private readonly IReadOnlyList<IRobotTransport> _transports;
    private IRobotTransport? _activeTransport;
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    public RobotConnectionManager(IEnumerable<IRobotTransport> transports)
    {
        _transports = transports
            .OrderBy(transport => transport.TransportKind == RobotTransportKind.Local ? 0 : 1)
            .ToArray();

        foreach (var transport in _transports)
        {
            transport.ConnectionStateChanged += state => OnTransportConnectionStateChanged(transport, state);
        }
    }

    public ConnectionState ConnectionState => _connectionState;

    public RobotTransportKind? ActiveTransportKind => _activeTransport?.TransportKind;

    public event Action<ConnectionState>? ConnectionStateChanged;

    public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        foreach (var transport in _transports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await transport.CheckAvailabilityAsync(cancellationToken))
                {
                    _activeTransport = transport;
                    return true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // An unavailable path must not prevent trying the next transport.
            }
        }

        return false;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        SetConnectionState(ConnectionState.Connecting);
        if (await CheckAvailabilityAsync(cancellationToken))
        {
            try
            {
                await _activeTransport!.ConnectAsync(cancellationToken);
                SetConnectionState(ConnectionState.Connected);
                return;
            }
            catch
            {
                _activeTransport = null;
                SetConnectionState(ConnectionState.Disconnected);
                throw;
            }
        }

        SetConnectionState(ConnectionState.Disconnected);
        throw new InvalidOperationException("A-Rigator could not be reached through any configured connection path.");
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_activeTransport is not null)
            await _activeTransport.DisconnectAsync(cancellationToken);

        _activeTransport = null;
        SetConnectionState(ConnectionState.Disconnected);
    }

    public async Task<RobotStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var transport = await GetActiveTransportAsync(cancellationToken);
        return await transport.GetStatusAsync(cancellationToken);
    }

    public async Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default)
    {
        var transport = await GetActiveTransportAsync(cancellationToken);
        await transport.SendCommandAsync(command, cancellationToken);
    }

    public async Task ApplyIrrigationProfileAsync(IrrigationProfile profile, CancellationToken cancellationToken = default)
    {
        var transport = await GetActiveTransportAsync(cancellationToken);
        await transport.ApplyIrrigationProfileAsync(profile, cancellationToken);
    }

    public async Task StartManualIrrigationAsync(CancellationToken cancellationToken = default)
    {
        var transport = await GetActiveTransportAsync(cancellationToken);
        await transport.StartManualIrrigationAsync(cancellationToken);
    }

    public async Task StopManualIrrigationAsync(CancellationToken cancellationToken = default)
    {
        var transport = await GetActiveTransportAsync(cancellationToken);
        await transport.StopManualIrrigationAsync(cancellationToken);
    }

    public void SetRobotAddress(string address)
    {
        var localTransport = _transports.FirstOrDefault(
            transport => transport.TransportKind == RobotTransportKind.Local);

        if (localTransport is null)
            throw new InvalidOperationException("No local robot transport is registered.");

        localTransport.SetRobotAddress(address);
    }

    private async Task<IRobotTransport> GetActiveTransportAsync(CancellationToken cancellationToken)
    {
        if (_activeTransport is not null && _activeTransport.ConnectionState == ConnectionState.Connected)
            return _activeTransport;

        if (await CheckAvailabilityAsync(cancellationToken))
        {
            await _activeTransport!.ConnectAsync(cancellationToken);
            SetConnectionState(ConnectionState.Connected);
            return _activeTransport;
        }

        SetConnectionState(ConnectionState.Disconnected);
        throw new InvalidOperationException("A-Rigator is not connected. Reconnect before sending commands.");
    }

    private void OnTransportConnectionStateChanged(IRobotTransport transport, ConnectionState state)
    {
        if (ReferenceEquals(transport, _activeTransport))
            SetConnectionState(state);
    }

    private void SetConnectionState(ConnectionState state)
    {
        if (_connectionState == state)
            return;

        _connectionState = state;
        ConnectionStateChanged?.Invoke(state);
    }
}
