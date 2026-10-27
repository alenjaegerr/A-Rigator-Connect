using Microsoft.Maui.Storage;

namespace A_Rigator_Controller.Services;

public sealed class SecureSessionStorage : ISecureSessionStorage
{
    private const string AccessTokenKey = "secure_access_token";
    private const string RefreshTokenKey = "secure_refresh_token";

    public async Task SaveAsync(string accessToken, string? refreshToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await SecureStorage.SetAsync(AccessTokenKey, accessToken);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await SecureStorage.SetAsync(RefreshTokenKey, refreshToken);
        }
        else
        {
            SecureStorage.Remove(RefreshTokenKey);
        }
    }

    public async Task<(string? AccessToken, string? RefreshToken)> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var accessToken = await SecureStorage.GetAsync(AccessTokenKey);
        var refreshToken = await SecureStorage.GetAsync(RefreshTokenKey);
        return (accessToken, refreshToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Run(() =>
        {
            try
            {
                SecureStorage.Remove(AccessTokenKey);
                SecureStorage.Remove(RefreshTokenKey);
            }
            catch
            {
                // Intentionally ignored; no-op fallback for unsupported platform storage.
            }
        }, cancellationToken);
    }
}
