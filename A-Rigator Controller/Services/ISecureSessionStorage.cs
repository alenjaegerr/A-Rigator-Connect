namespace A_Rigator_Controller.Services;

public interface ISecureSessionStorage
{
    Task SaveAsync(string accessToken, string? refreshToken, CancellationToken cancellationToken = default);

    Task<(string? AccessToken, string? RefreshToken)> LoadAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
