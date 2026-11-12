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
    private bool _isBusy;

    public WelcomeViewModel(
        IUserProfileService userProfileService,
        IAccountService accountService)
    {
        _userProfileService = userProfileService;
        _accountService = accountService;
        SelectLocalCommand = new Command(SelectLocal, () => !IsBusy);
        SelectGoogleCommand = new Command(async () => await SignInWithGoogleAsync(), () => !IsBusy);
        ContinueCommand = new Command(SaveDisplayName, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? DisplayNameSaved;

    public ICommand ContinueCommand { get; }

    public ICommand SelectLocalCommand { get; }

    public ICommand SelectGoogleCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotBusy));
            RefreshCommands();
        }
    }

    public bool IsNotBusy => !IsBusy;

    public bool IsNameEntryVisible
    {
        get => _isNameEntryVisible;
        private set
        {
            if (_isNameEntryVisible == value) return;
            _isNameEntryVisible = value;
            OnPropertyChanged();
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value) return;
            _displayName = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    private void SelectLocal()
    {
        if (IsBusy) return;

        // Local mode should begin robot discovery immediately. The user can
        // customize their account name later from Account settings.
        var existingName = _userProfileService.GetDisplayName();
        if (string.IsNullOrWhiteSpace(existingName))
        {
            DisplayName = "Local User";
            _userProfileService.SaveDisplayName(DisplayName);
        }

        StatusMessage = string.Empty;
        DisplayNameSaved?.Invoke(this, EventArgs.Empty);
    }

    private async Task SignInWithGoogleAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Opening Google sign-in…";
        try
        {
            await _accountService.SignInWithGoogleAsync();

            var accountName = _accountService.DisplayName;
            if (!string.IsNullOrWhiteSpace(accountName))
            {
                DisplayName = accountName.Trim();
                _userProfileService.SaveDisplayName(DisplayName);
            }

            StatusMessage = string.Empty;
            DisplayNameSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Google sign-in was cancelled. You can try again or continue locally.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Google sign-in failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SaveDisplayName()
    {
        if (IsBusy) return;

        var displayName = DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            StatusMessage = "Enter a name to continue.";
            return;
        }

        if (displayName.Length > 48)
        {
            StatusMessage = "Your name must be 48 characters or fewer.";
            return;
        }

        _userProfileService.SaveDisplayName(displayName);
        StatusMessage = string.Empty;
        DisplayNameSaved?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshCommands()
    {
        (SelectLocalCommand as Command)?.ChangeCanExecute();
        (SelectGoogleCommand as Command)?.ChangeCanExecute();
        (ContinueCommand as Command)?.ChangeCanExecute();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
