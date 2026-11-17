using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

/// <summary>
/// A single robot communication path. Implementations own their transport
/// details; application pages should depend on IRobotCommunicationService.
/// </summary>
public interface IRobotTransport
{
    RobotTransportKind TransportKind { get; }
    ConnectionState ConnectionState { get; }
    event Action<ConnectionState>? ConnectionStateChanged;
    Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default);
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<RobotStatus?> GetStatusAsync(CancellationToken cancellationToken = default);
    Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default);
    Task ApplyIrrigationProfileAsync(IrrigationProfile profile, CancellationToken cancellationToken = default);
    Task StartManualIrrigationAsync(CancellationToken cancellationToken = default);
    Task StopManualIrrigationAsync(CancellationToken cancellationToken = default);
    void SetRobotAddress(string address);
}
