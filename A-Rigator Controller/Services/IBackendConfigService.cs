namespace A_Rigator_Controller.Services;

public interface IBackendConfigService
{
    string? ApiBaseUrl { get; }

    bool IsConfigured { get; }

    void LoadFromPreferences();

    void Save(string apiBaseUrl);

    void Clear();
}
