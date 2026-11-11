using Microsoft.Maui.Networking;

namespace A_Rigator_Controller.Services;

public sealed class IrrigationProfileSyncCoordinator
{
    private readonly IrrigationProfileService _profiles;
    private readonly IIrrigationProfileCloudService _cloud;
    private readonly IAccountService _account;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;

    public IrrigationProfileSyncCoordinator(IrrigationProfileService profiles,
        IIrrigationProfileCloudService cloud, IAccountService account)
    {
        _profiles = profiles;
        _cloud = cloud;
        _account = account;
    }

    public event EventHandler<string>? StatusChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return;
        _started = true;
        _profiles.ProfilesChanged += OnProfilesChanged;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

        try { await _account.InitializeSessionAsync(cancellationToken); }
        catch { /* Keep the app fully usable offline; a later connectivity change retries. */ }

        if (_account.IsAuthenticated)
            await SyncNowAsync(cancellationToken);
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_account.IsAuthenticated)
        {
            StatusChanged?.Invoke(this, "Sign in to sync irrigation profiles to your account.");
            return;
        }
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            StatusChanged?.Invoke(this, "Offline — irrigation profiles are saved locally and will sync when internet returns.");
            return;
        }
        if (!await _gate.WaitAsync(0, cancellationToken)) return;

        try
        {
            StatusChanged?.Invoke(this, "Syncing irrigation profiles…");
            await _cloud.SyncProfilesAsync(_profiles.Profiles.ToList(), cancellationToken);
            StatusChanged?.Invoke(this, $"Irrigation profiles synced to your account ({_profiles.Profiles.Count}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Sync pending — {ex.Message}");
        }
        finally { _gate.Release(); }
    }

    private async void OnProfilesChanged(object? sender, EventArgs e)
    {
        if (_account.IsAuthenticated)
            await SyncNowAsync();
        else
            StatusChanged?.Invoke(this, "Saved locally. Sign in to sync these irrigation profiles to your account.");
    }

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet && _account.IsAuthenticated)
            await SyncNowAsync();
    }
}
