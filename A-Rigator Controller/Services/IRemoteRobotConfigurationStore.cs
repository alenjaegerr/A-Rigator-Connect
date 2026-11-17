using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public interface IRemoteRobotConfigurationStore
{
    Task<RemoteRobotConnectionOptions?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RemoteRobotConnectionOptions options, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}