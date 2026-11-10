using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using A_Rigator_Controller.Services;

namespace A_Rigator_Controller.ViewModels;

public sealed class AccountViewModel : INotifyPropertyChanged
{
    private readonly IAccountService _accountService;
    private readonly ISupabaseConfigService _supabaseConfigService;
    private readonly IBackendConfigService _backendConfigService;
    private string _supabaseUrl = string.Empty;
    private string _supabaseAnonKey = string.Empty;
    private string _backendApiUrl = string.Empty;
    private string _statusMessage = "Local mode is active. Remote mode and cloud sync remain locked until Google auth is complete.";

    public AccountViewModel(IAccountService accountService, ISupabaseConfigService supabaseConfigService, IBackendConfigService backendConfigService)
    {
        _accountService = accountService;
        _supabaseConfigService = supabaseConfigService;
        _backendConfigService = backendConfigService;
        SignInCommand = new Command(async () => await SignInAsync());
        SignOutCommand = new Command(async () => await SignOutAsync());
        SaveSupabaseConfigCommand = new Command(SaveSupabaseConfig);
        SaveBackendConfigCommand = new Command(SaveBackendConfig);

        _supabaseUrl = _supabaseConfigService.Url ?? string.Empty;
        _supabaseAnonKey = _supabaseConfigService.AnonKey ?? string.Empty;
        _backendApiUrl = _backendConfigService.ApiBaseUrl ?? string.Empty;

        _ = InitializeAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SupabaseUrl
    {
        get => _supabaseUrl;
        set
        {
            if (_supabaseUrl == value)
            {
                return;
            }

            _supabaseUrl = value;
            OnPropertyChanged();
        }
    }

    public string SupabaseAnonKey
    {
        get => _supabaseAnonKey;
        set
        {
            if (_supabaseAnonKey == value)
            {
                return;
            }

            _supabaseAnonKey = value;
            OnPropertyChanged();
        }
    }

    public string BackendApiUrl
    {
        get => _backendApiUrl;
        set
        {
            if (_backendApiUrl == value)
            {
                return;
            }

            _backendApiUrl = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsAuthenticated => _accountService.IsAuthenticated;

    public bool IsRemoteModeAllowed => _accountService.IsRemoteModeAllowed;

    public bool IsSupabaseConfigured => _supabaseConfigService.IsConfigured;

    public bool IsBackendConfigured => _backendConfigService.IsConfigured;

    public string AccountSummary => IsAuthenticated
        ? $"Signed in as {_accountService.DisplayName ?? "Google user"}"
        : "Not signed in with Google";

    public ICommand SignInCommand { get; }

    public ICommand SignOutCommand { get; }

    public ICommand SaveSupabaseConfigCommand { get; }

    public ICommand SaveBackendConfigCommand { get; }

    private async Task SignInAsync()
    {
        try
        {
            await _accountService.SignInWithGoogleAsync();
            StatusMessage = _backendConfigService.IsConfigured
                ? "Google authentication succeeded and its session is stored securely. Remote relay and cloud telemetry still require deployed backend endpoints."
                : "Google authentication succeeded. Configure the backend URL before enabling remote-mode prerequisites.";
            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(IsRemoteModeAllowed));
            OnPropertyChanged(nameof(AccountSummary));
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task SignOutAsync()
    {
        await _accountService.SignOutAsync();
        StatusMessage = "Signed out. Local mode remains available.";
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(IsRemoteModeAllowed));
        OnPropertyChanged(nameof(AccountSummary));
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _accountService.InitializeSessionAsync();
            StatusMessage = IsAuthenticated
                ? (_backendConfigService.IsConfigured
                    ? "Your secure Google session was restored. Remote backend configuration is present; remote relay services still need deployment."
                    : "Your secure Google session was restored. Add the backend API URL to complete remote-mode configuration.")
                : "Local mode is active. Sign in with Google only if you need account-linked features.";
            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(IsRemoteModeAllowed));
            OnPropertyChanged(nameof(AccountSummary));
        }
        catch
        {
            StatusMessage = "Local mode is active. Remote mode and cloud sync remain locked until Google auth is complete.";
        }
    }

    private void SaveSupabaseConfig()
    {
        if (string.IsNullOrWhiteSpace(SupabaseUrl) || string.IsNullOrWhiteSpace(SupabaseAnonKey))
        {
            StatusMessage = "Enter both the Supabase URL and anon key to enable the backend scaffold.";
            return;
        }

        _supabaseConfigService.Save(SupabaseUrl, SupabaseAnonKey);
        StatusMessage = "Supabase settings saved. Google auth and backend URL are now required before remote mode can unlock.";
        OnPropertyChanged(nameof(IsSupabaseConfigured));
        OnPropertyChanged(nameof(IsRemoteModeAllowed));
    }

    private void SaveBackendConfig()
    {
        if (string.IsNullOrWhiteSpace(BackendApiUrl))
        {
            StatusMessage = "Enter the backend API base URL to configure remote services.";
            return;
        }

        try
        {
            _backendConfigService.Save(BackendApiUrl);
            StatusMessage = "Backend URL saved. Remote relay and cloud telemetry still require deployed backend endpoints.";
            OnPropertyChanged(nameof(IsBackendConfigured));
            OnPropertyChanged(nameof(IsRemoteModeAllowed));
        }
        catch (ArgumentException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
