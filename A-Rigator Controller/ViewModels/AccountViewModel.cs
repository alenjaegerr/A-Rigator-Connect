using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using A_Rigator_Controller.Services;

namespace A_Rigator_Controller.ViewModels;

public sealed class AccountViewModel : INotifyPropertyChanged
{
    private readonly IAccountService _accountService;
    private readonly ISupabaseConfigService _supabaseConfigService;
    private string _supabaseUrl = string.Empty;
    private string _supabaseAnonKey = string.Empty;
    private string _statusMessage = "Local mode is active. Remote mode and cloud sync remain locked until Google auth is complete.";

    public AccountViewModel(IAccountService accountService, ISupabaseConfigService supabaseConfigService)
    {
        _accountService = accountService;
        _supabaseConfigService = supabaseConfigService;
        SignInCommand = new Command(async () => await SignInAsync());
        SignOutCommand = new Command(async () => await SignOutAsync());
        SaveSupabaseConfigCommand = new Command(SaveSupabaseConfig);

        _supabaseUrl = _supabaseConfigService.Url ?? string.Empty;
        _supabaseAnonKey = _supabaseConfigService.AnonKey ?? string.Empty;
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

    public string AccountSummary => IsAuthenticated
        ? $"Signed in as {_accountService.DisplayName ?? "Google user"}"
        : "Not signed in with Google";

    public ICommand SignInCommand { get; }

    public ICommand SignOutCommand { get; }

    public ICommand SaveSupabaseConfigCommand { get; }

    private async Task SignInAsync()
    {
        try
        {
            await _accountService.SignInWithGoogleAsync();
            StatusMessage = "Google auth is ready. Remote mode and cloud sync are unlocked once Supabase is configured and linked.";
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

    private void SaveSupabaseConfig()
    {
        if (string.IsNullOrWhiteSpace(SupabaseUrl) || string.IsNullOrWhiteSpace(SupabaseAnonKey))
        {
            StatusMessage = "Enter both the Supabase URL and anon key to enable the backend scaffold.";
            return;
        }

        _supabaseConfigService.Save(SupabaseUrl, SupabaseAnonKey);
        StatusMessage = "Supabase settings saved. Google auth can unlock remote mode and cloud sync when the backend is ready.";
        OnPropertyChanged(nameof(IsSupabaseConfigured));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
