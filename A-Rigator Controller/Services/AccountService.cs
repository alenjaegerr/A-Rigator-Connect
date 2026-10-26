namespace A_Rigator_Controller.Services;

public sealed class AccountService : IAccountService
{
    private readonly ISupabaseConfigService _supabaseConfigService;

    public AccountService(ISupabaseConfigService supabaseConfigService)
    {
        _supabaseConfigService = supabaseConfigService;
        _supabaseConfigService.LoadFromPreferences();
    }

    public bool IsAuthenticated { get; private set; }

    public string? GoogleUserId { get; private set; }

    public string? DisplayName { get; private set; }

    public string? Email { get; private set; }

    public bool IsRemoteModeAllowed => IsAuthenticated && _supabaseConfigService.IsConfigured;

    public Task SignInWithGoogleAsync(CancellationToken cancellationToken = default)
    {
        if (!_supabaseConfigService.IsConfigured)
        {
            throw new InvalidOperationException("Supabase must be configured before Google auth can be used for remote mode.");
        }

        // Scaffold only: replace this with actual Google OAuth once credentials and backend are ready.
        IsAuthenticated = true;
        GoogleUserId = "google-user-placeholder";
        DisplayName = "Google User";
        Email = "user@example.com";
        return Task.CompletedTask;
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        IsAuthenticated = false;
        GoogleUserId = null;
        DisplayName = null;
        Email = null;
        return Task.CompletedTask;
    }

    public Task RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        // Placeholder provider keeps the app architecture ready for real auth and token refresh.
        return Task.CompletedTask;
    }
}
