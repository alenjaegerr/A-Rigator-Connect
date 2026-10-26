using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public interface IRobotCommunicationService
{
    ConnectionState ConnectionState { get; }

    event Action<ConnectionState>? ConnectionStateChanged;

    Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<RobotStatus?> GetStatusAsync(CancellationToken cancellationToken = default);

    Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default);
}
