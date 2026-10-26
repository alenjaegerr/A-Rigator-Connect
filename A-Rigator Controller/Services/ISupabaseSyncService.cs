namespace A_Rigator_Controller.Services;

public interface ISupabaseSyncService
{
    bool IsCloudSyncEnabled { get; }

    bool IsRemoteModeAvailable { get; }

    Task<bool> SyncRobotProfileAsync(string robotId, string payload, CancellationToken cancellationToken = default);

    Task<bool> SyncUserProfileAsync(string userId, string payload, CancellationToken cancellationToken = default);
}
