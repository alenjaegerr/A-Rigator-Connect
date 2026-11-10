namespace A_Rigator_Controller.Services;

public sealed class BackendConfigService : IBackendConfigService
{
    private const string ApiBaseUrlKey = "backend_api_base_url";

    public string? ApiBaseUrl { get; private set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiBaseUrl) &&
        Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public void LoadFromPreferences()
    {
        ApiBaseUrl = Preferences.Default.Get<string?>(ApiBaseUrlKey, null);
    }

    public void Save(string apiBaseUrl)
    {
        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Enter a valid HTTP or HTTPS backend URL.", nameof(apiBaseUrl));

        ApiBaseUrl = uri.ToString().TrimEnd('/');
        Preferences.Default.Set(ApiBaseUrlKey, ApiBaseUrl);
    }

    public void Clear()
    {
        ApiBaseUrl = null;
        Preferences.Default.Remove(ApiBaseUrlKey);
    }
}