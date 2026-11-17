using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

/// <summary>
/// Coordinates robot communication paths. Remote IoT is preferred; local
/// transports (BLE after the AP migration) are used when IoT is unavailable.
/// </summary>
public sealed class RobotConnectionManager : IRobotCommunicationService
{
    private readonly IReadOnlyList<IRobotTransport> _transports;
    private IRobotTransport? _activeTransport;
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    public RobotConnectionManager(IEnumerable<IRobotTransport> transports)
    {
        _transports = transports
            .OrderBy(transport => transport.TransportKind == RobotTransportKind.Remote ? 0 : 1)
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
        await ConnectPreferredAvailableTransportAsync(cancellationToken);
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
        // Address configuration belongs to a local transport. Remote IoT is
        // addressed by the provisioned robot identity, not a LAN address.
        var localTransport = _transports.FirstOrDefault(
            transport => transport.TransportKind == RobotTransportKind.Local);

        localTransport?.SetRobotAddress(address);
    }

    private async Task<IRobotTransport> GetActiveTransportAsync(CancellationToken cancellationToken)
    {
        if (_activeTransport is not null && _activeTransport.ConnectionState == ConnectionState.Connected)
            return _activeTransport;

        return await ConnectPreferredAvailableTransportAsync(cancellationToken);
    }

    private async Task<IRobotTransport> ConnectPreferredAvailableTransportAsync(
        CancellationToken cancellationToken)
    {
        SetConnectionState(ConnectionState.Connecting);
        Exception? lastError = null;

        // _transports are ordered IoT first, then local BLE fallback.
        foreach (var transport in _transports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (transport.ConnectionState == ConnectionState.Connected)
                {
                    _activeTransport = transport;
                    SetConnectionState(ConnectionState.Connected);
                    return transport;
                }

                if (!await transport.CheckAvailabilityAsync(cancellationToken))
                    continue;

                await transport.ConnectAsync(cancellationToken);
                _activeTransport = transport;
                SetConnectionState(ConnectionState.Connected);
                return transport;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetConnectionState(ConnectionState.Disconnected);
                throw;
            }
            catch (Exception error)
            {
                lastError = error;
                if (ReferenceEquals(_activeTransport, transport))
                    _activeTransport = null;
                // A configured but unreachable IoT broker must not block BLE.
            }
        }

        SetConnectionState(ConnectionState.Disconnected);
        throw new InvalidOperationException(
            "A-Rigator could not be reached over IoT or Bluetooth. Check the robot and try reconnecting.",
            lastError);
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
