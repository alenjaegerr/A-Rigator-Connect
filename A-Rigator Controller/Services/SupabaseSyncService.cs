namespace A_Rigator_Controller.Services;

public sealed class SupabaseSyncService : ISupabaseSyncService
{
    private readonly IAccountService _accountService;

    public SupabaseSyncService(IAccountService accountService)
    {
        _accountService = accountService;
    }

    public bool IsCloudSyncEnabled => _accountService.IsAuthenticated;

    public bool IsRemoteModeAvailable => _accountService.IsRemoteModeAllowed;

    public Task<bool> SyncRobotProfileAsync(string robotId, string payload, CancellationToken cancellationToken = default)
    {
        if (!_accountService.IsRemoteModeAllowed)
        {
            return Task.FromResult(false);
        }

        // Scaffold only: replace with actual Supabase PostgREST or Realtime sync once backend is ready.
        return Task.FromResult(true);
    }

    public Task<bool> SyncUserProfileAsync(string userId, string payload, CancellationToken cancellationToken = default)
    {
        if (!_accountService.IsRemoteModeAllowed)
        {
            return Task.FromResult(false);
        }

        // Scaffold only: replace with actual Supabase profile write once backend is ready.
        return Task.FromResult(true);
    }
}
