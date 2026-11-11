namespace A_Rigator_Controller.Services;

public sealed class SupabaseConfigService : ISupabaseConfigService
{
    private const string UrlKey = "supabase_url";
    private const string AnonKeyKey = "supabase_anon_key";

    public SupabaseConfigService() => LoadFromPreferences();

    public string? Url { get; private set; }

    public string? AnonKey { get; private set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AnonKey) &&
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public void LoadFromPreferences()
    {
        Url = Preferences.Default.Get<string?>(UrlKey, null);
        AnonKey = Preferences.Default.Get<string?>(AnonKeyKey, null);
    }

    public void Save(string url, string anonKey)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Enter a valid Supabase HTTP or HTTPS project URL.", nameof(url));

        if (string.IsNullOrWhiteSpace(anonKey))
            throw new ArgumentException("Enter the Supabase anon/public key.", nameof(anonKey));

        Url = uri.ToString().TrimEnd('/');
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