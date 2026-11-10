namespace A_Rigator_Controller.Services;

public sealed class SupabaseSyncService : ISupabaseSyncService
{
    public SupabaseSyncService(IAccountService accountService)
    {
        // Authentication and backend configuration are not a cloud-sync implementation.
    }

    // Keep cloud features disabled until real, authenticated endpoints are deployed.
    public bool IsCloudSyncEnabled => false;

    public bool IsRemoteModeAvailable => false;

    public Task<bool> SyncRobotProfileAsync(string robotId, string payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task<bool> SyncUserProfileAsync(string userId, string payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}