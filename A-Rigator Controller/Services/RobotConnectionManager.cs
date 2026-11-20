using A_Rigator_Controller.Models;
using Microsoft.Maui.Storage;

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
    private RobotConnectionMode _preferredConnectionMode =
        Enum.TryParse<RobotConnectionMode>(Preferences.Get("arigator_preferred_connection_mode", "BLE"), true, out var savedMode)
            ? savedMode
            : RobotConnectionMode.BLE;

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

    public RobotConnectionMode PreferredConnectionMode
    {
        get => _preferredConnectionMode;
        set
        {
            if (value is not RobotConnectionMode.BLE and not RobotConnectionMode.IoT ||
                _preferredConnectionMode == value) return;
            _preferredConnectionMode = value;
            Preferences.Set("arigator_preferred_connection_mode", value.ToString());
            _activeTransport = null;
            SetConnectionState(ConnectionState.Disconnected);
        }
    }

    public RobotTransportKind? ActiveTransportKind => _activeTransport?.TransportKind;

    public event Action<ConnectionState>? ConnectionStateChanged;

    public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var transport = GetPreferredTransport();
        if (transport is null) return false;
        try
        {
            var available = await transport.CheckAvailabilityAsync(cancellationToken);
            if (available) _activeTransport = transport;
            return available;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
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
        var status = await transport.GetStatusAsync(cancellationToken);
        if (status is not null)
        {
            status.ConnectionMode = PreferredConnectionMode;
            SetConnectionState(ConnectionState.Connected);
        }
        else
        {
            _activeTransport = null;
            SetConnectionState(ConnectionState.Disconnected);
        }
        return status;
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

    private async Task<IRobotTransport?> TryConnectLocalTransportAsync(
        CancellationToken cancellationToken)
    {
        var localTransport = _transports.FirstOrDefault(
            transport => transport.TransportKind == RobotTransportKind.Local);
        if (localTransport is null)
            return null;

        try
        {
            if (localTransport.ConnectionState != ConnectionState.Connected)
            {
                if (!await localTransport.CheckAvailabilityAsync(cancellationToken))
                    return null;

                await localTransport.ConnectAsync(cancellationToken);
            }

            _activeTransport = localTransport;
            SetConnectionState(ConnectionState.Connected);
            return localTransport;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (ReferenceEquals(_activeTransport, localTransport))
                _activeTransport = null;
            return null;
        }
    }

    private async Task<IRobotTransport> GetActiveTransportAsync(CancellationToken cancellationToken)
    {
        var preferred = GetPreferredTransport();
        if (preferred is null)
            throw new InvalidOperationException("The selected connection mode is unavailable.");

        if (_activeTransport == preferred && preferred.ConnectionState == ConnectionState.Connected)
        {
            if (preferred.TransportKind == RobotTransportKind.Local)
                await preferred.ConnectAsync(cancellationToken);
            return preferred;
        }

        return await ConnectPreferredAvailableTransportAsync(cancellationToken);
    }

    private IRobotTransport? GetPreferredTransport() => _transports.FirstOrDefault(
        transport => PreferredConnectionMode == RobotConnectionMode.IoT
            ? transport.TransportKind == RobotTransportKind.Remote
            : transport.TransportKind == RobotTransportKind.Local);

    private async Task<IRobotTransport> ConnectPreferredAvailableTransportAsync(CancellationToken cancellationToken)
    {
        SetConnectionState(ConnectionState.Connecting);
        var transport = GetPreferredTransport();
        try
        {
            if (transport is null || !await transport.CheckAvailabilityAsync(cancellationToken))
                throw new InvalidOperationException(PreferredConnectionMode == RobotConnectionMode.BLE
                    ? "Bluetooth mode is selected. Pair with A-Rigator over Bluetooth first."
                    : "Remote mode is selected, but no remote robot connection is configured.");

            if (transport.ConnectionState != ConnectionState.Connected)
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
        catch
        {
            _activeTransport = null;
            SetConnectionState(ConnectionState.Disconnected);
            throw;
        }
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
