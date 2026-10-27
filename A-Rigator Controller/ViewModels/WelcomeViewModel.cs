using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using A_Rigator_Controller.Services;

namespace A_Rigator_Controller.ViewModels;

public sealed class WelcomeViewModel : INotifyPropertyChanged
{
    private readonly IUserProfileService _userProfileService;
    private readonly IAccountService _accountService;
    private string _displayName = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isNameEntryVisible;

    public WelcomeViewModel(IUserProfileService userProfileService, IAccountService accountService)
    {
        _userProfileService = userProfileService;
        _accountService = accountService;
        SelectLocalCommand = new Command(SelectLocal);
        SelectGoogleCommand = new Command(async () => await SelectGoogleAsync());
        ContinueCommand = new Command(SaveDisplayName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? DisplayNameSaved;

    public ICommand ContinueCommand { get; }

    public ICommand SelectLocalCommand { get; }

    public ICommand SelectGoogleCommand { get; }

    public bool IsNameEntryVisible
    {
        get => _isNameEntryVisible;
        private set
        {
            if (_isNameEntryVisible == value)
            {
                return;
            }

            _isNameEntryVisible = value;
            OnPropertyChanged();
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value)
            {
                return;
            }

            _displayName = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetStatusMessage(value);
    }

    private void SelectLocal()
    {
        StatusMessage = string.Empty;
        IsNameEntryVisible = true;
    }

    private async Task SelectGoogleAsync()
    {
        try
        {
            await _accountService.SignInWithGoogleAsync();
            StatusMessage = "Google sign-in complete. Enter a name to continue.";
            IsNameEntryVisible = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"GOOGLE SIGN-IN ERROR: {ex}");

            StatusMessage = ex.Message;
            IsNameEntryVisible = false;
        }
    }

    private void SaveDisplayName()
    {
        var displayName = DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            StatusMessage = "Enter a name to continue.";
            return;
        }

        _userProfileService.SaveDisplayName(displayName);
        StatusMessage = string.Empty;
        DisplayNameSaved?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatusMessage(string value)
    {
        if (_statusMessage == value)
        {
            return;
        }

        _statusMessage = value;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
