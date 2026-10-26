using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public sealed class UnavailableRobotCommunicationService : IRobotCommunicationService
{
    public ConnectionState ConnectionState => ConnectionState.Disconnected;

    public event Action<ConnectionState>? ConnectionStateChanged;

    public Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<RobotStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<RobotStatus?>(null);
    }

    public Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Robot controls are unavailable.");
    }
}
