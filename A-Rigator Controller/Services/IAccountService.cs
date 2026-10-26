namespace A_Rigator_Controller.Services;

public interface IAccountService
{
    bool IsAuthenticated { get; }

    string? GoogleUserId { get; }

    string? DisplayName { get; }

    string? Email { get; }

    bool IsRemoteModeAllowed { get; }

    Task SignInWithGoogleAsync(CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);

    Task RefreshStateAsync(CancellationToken cancellationToken = default);
}
