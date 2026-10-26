namespace A_Rigator_Controller.Services;

public interface ISupabaseConfigService
{
    string? Url { get; }

    string? AnonKey { get; }

    bool IsConfigured { get; }

    void LoadFromPreferences();

    void Save(string url, string anonKey);

    void Clear();
}
