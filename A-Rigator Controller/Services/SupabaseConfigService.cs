
namespace A_Rigator_Controller.Services;

public sealed class SupabaseConfigService : ISupabaseConfigService
{
    // DEMO ONLY: replace these with your Supabase project values.
    private const string DemoSupabaseUrl = "https://pcgbyowcqpgtkfbdfwfb.supabase.co";
    private const string DemoSupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InBjZ2J5b3djcXBndGtmYmRmd2ZiIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTI5NzI1NDMsImV4cCI6MjEwODU0ODU0M30.gSzwf5ReakXUmtrX2JsYCLVvmKqiaGJrbDloeULVvmk";

    private const string UrlKey = "https://pcgbyowcqpgtkfbdfwfb.supabase.co";
    private const string AnonKeyKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InBjZ2J5b3djcXBndGtmYmRmd2ZiIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTI5NzI1NDMsImV4cCI6MjEwODU0ODU0M30.gSzwf5ReakXUmtrX2JsYCLVvmKqiaGJrbDloeULVvmk";

    public string? Url { get; private set; }
    public string? AnonKey { get; private set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url) &&
        !string.IsNullOrWhiteSpace(AnonKey) &&
        Uri.TryCreate(Url, UriKind.Absolute, out _);

    public void LoadFromPreferences()
    {
        // Existing saved configuration takes priority.
        Url = Preferences.Default.Get<string?>(UrlKey, null);
        AnonKey = Preferences.Default.Get<string?>(AnonKeyKey, null);

        if (string.IsNullOrWhiteSpace(Url) ||
            string.IsNullOrWhiteSpace(AnonKey))
        {
            Url = DemoSupabaseUrl;
            AnonKey = DemoSupabaseAnonKey;
        }
    }

    public void Save(string url, string anonKey)
    {
        Url = url.Trim().TrimEnd('/');
        AnonKey = anonKey.Trim();

        Preferences.Default.Set(UrlKey, Url);
        Preferences.Default.Set(AnonKeyKey, AnonKey);
    }

    public void Clear()
    {
        Preferences.Default.Remove(UrlKey);
        Preferences.Default.Remove(AnonKeyKey);

        // Restore demo configuration instead of leaving sign-in unconfigured.
        Url = DemoSupabaseUrl;
        AnonKey = DemoSupabaseAnonKey;
    }
}
