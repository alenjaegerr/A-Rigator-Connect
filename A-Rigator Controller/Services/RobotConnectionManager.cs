using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

/// <summary>
/// Application-facing connection coordinator. It keeps pages independent
/// from HTTP/MQTT implementation details and is the future selection point
/// for local and remote transports.
/// </summary>
public sealed class RobotConnectionManager : IRobotCommunicationService
{
    private readonly IRobotTransport _localTransport;

    public RobotConnectionManager(IRobotTransport localTransport)
    {
        _localTransport = localTransport;
        _localTransport.ConnectionStateChanged += OnTransportConnectionStateChanged;
    }

    public ConnectionState ConnectionState => _localTransport.ConnectionState;

    public event Action<ConnectionState>? ConnectionStateChanged;

    public Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default) =>
        _localTransport.CheckAvailabilityAsync(cancellationToken);

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        _localTransport.ConnectAsync(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        _localTransport.DisconnectAsync(cancellationToken);

    public Task<RobotStatus?> GetStatusAsync(CancellationToken cancellationToken = default) =>
        _localTransport.GetStatusAsync(cancellationToken);

    public Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default) =>
        _localTransport.SendCommandAsync(command, cancellationToken);

    public Task ApplyIrrigationProfileAsync(IrrigationProfile profile, CancellationToken cancellationToken = default) =>
        _localTransport.ApplyIrrigationProfileAsync(profile, cancellationToken);

    public Task StartManualIrrigationAsync(CancellationToken cancellationToken = default) =>
        _localTransport.StartManualIrrigationAsync(cancellationToken);

    public Task StopManualIrrigationAsync(CancellationToken cancellationToken = default) =>
        _localTransport.StopManualIrrigationAsync(cancellationToken);

    public void SetRobotAddress(string address) =>
        _localTransport.SetRobotAddress(address);

    private void OnTransportConnectionStateChanged(ConnectionState state) =>
        ConnectionStateChanged?.Invoke(state);
}
