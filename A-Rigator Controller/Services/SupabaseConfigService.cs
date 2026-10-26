namespace A_Rigator_Controller.Services;

public sealed class SupabaseConfigService : ISupabaseConfigService
{
    private const string UrlKey = "supabase_url";
    private const string AnonKeyKey = "supabase_anon_key";

    public string? Url { get; private set; }

    public string? AnonKey { get; private set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(AnonKey);

    public void LoadFromPreferences()
    {
        Url = Preferences.Default.Get<string?>(UrlKey, null);
        AnonKey = Preferences.Default.Get<string?>(AnonKeyKey, null);
    }

    public void Save(string url, string anonKey)
    {
        Url = url.Trim();
        AnonKey = anonKey.Trim();

        Preferences.Default.Set(UrlKey, Url);
        Preferences.Default.Set(AnonKeyKey, AnonKey);
    }

    public void Clear()
    {
        Url = null;
        AnonKey = null;
        Preferences.Default.Remove(UrlKey);
        Preferences.Default.Remove(AnonKeyKey);
    }
}
