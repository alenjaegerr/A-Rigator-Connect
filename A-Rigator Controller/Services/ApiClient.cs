using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace A_Rigator_Controller.Services;

public sealed class ApiClient : IApiClient
{
    private readonly HttpClient _httpClient = new();
    private readonly IBackendConfigService _backendConfigService;
    private readonly ISecureSessionStorage _secureSessionStorage;

    public ApiClient(IBackendConfigService backendConfigService, ISecureSessionStorage secureSessionStorage)
    {
        _backendConfigService = backendConfigService;
        _secureSessionStorage = secureSessionStorage;
    }

    public async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken = default)
    {
        var baseUrl = _backendConfigService.ApiBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Backend API base URL is not configured.");
        }

        var token = await GetBearerTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(baseUrl, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Backend request failed: {(int)response.StatusCode} - {payload}");
        }

        if (string.IsNullOrWhiteSpace(payload) || payload == "null")
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload);
    }

    public async Task<T?> PostAsync<T>(string relativePath, object payload, CancellationToken cancellationToken = default)
    {
        var baseUrl = _backendConfigService.ApiBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Backend API base URL is not configured.");
        }

        var token = await GetBearerTokenAsync(cancellationToken);
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(baseUrl, relativePath))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Backend request failed: {(int)response.StatusCode} - {result}");
        }

        if (string.IsNullOrWhiteSpace(result) || result == "null")
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(result);
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        var result = await _secureSessionStorage.LoadAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(result.AccessToken);
    }

    private async Task<string> GetBearerTokenAsync(CancellationToken cancellationToken)
    {
        var session = await _secureSessionStorage.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(session.AccessToken))
        {
            throw new InvalidOperationException("The user is not authenticated with a valid session token.");
        }

        return session.AccessToken;
    }

    private static string Combine(string baseUrl, string relativePath)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        var trimmedRelative = relativePath.TrimStart('/');
        return $"{trimmedBase}/{trimmedRelative}";
    }
}
