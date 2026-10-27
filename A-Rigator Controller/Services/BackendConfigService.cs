namespace A_Rigator_Controller.Services;

public sealed class BackendConfigService : IBackendConfigService
{
    private const string ApiBaseUrlKey = "backend_api_base_url";

    public string? ApiBaseUrl { get; private set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiBaseUrl);

    public void LoadFromPreferences()
    {
        ApiBaseUrl = Preferences.Default.Get<string?>(ApiBaseUrlKey, null);
    }

    public void Save(string apiBaseUrl)
    {
        ApiBaseUrl = apiBaseUrl.Trim();
        Preferences.Default.Set(ApiBaseUrlKey, ApiBaseUrl);
    }

    public void Clear()
    {
        ApiBaseUrl = null;
        Preferences.Default.Remove(ApiBaseUrlKey);
    }
}
