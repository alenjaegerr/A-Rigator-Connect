namespace A_Rigator_Controller.Services;

public interface IApiClient
{
    Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken = default);

    Task<T?> PostAsync<T>(string relativePath, object payload, CancellationToken cancellationToken = default);

    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);
}
