using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using A_Rigator_Controller.Services;

namespace A_Rigator_Controller.ViewModels;

public sealed class RemoteWifiSetupViewModel : INotifyPropertyChanged
{
    private readonly IAccountService _accountService;
    private string _statusMessage = "Sign in to connect your A-Rigator to the internet and enable remote access.";
    private bool _isBusy;

    public RemoteWifiSetupViewModel(IAccountService accountService)
    {
        _accountService = accountService;
        NotNowCommand = new Command(() => NotNowRequested?.Invoke(this, EventArgs.Empty));
        ContinueCommand = new Command(async () => await BeginRemoteSetupAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RemoteSetupRequested;
    public event EventHandler? NotNowRequested;

    public ICommand NotNowCommand { get; }
    public ICommand ContinueCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotBusy));
            (ContinueCommand as Command)?.ChangeCanExecute();
        }
    }

    public bool IsNotBusy => !IsBusy;
    public string ButtonText => _accountService.IsAuthenticated ? "CONTINUE SETUP" : "SIGN IN WITH GOOGLE";

    public string StatusMessage
    {
        get => _statusMessage;
        private set { if (_statusMessage == value) return; _statusMessage = value; OnPropertyChanged(); }
    }

    private async Task BeginRemoteSetupAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (!_accountService.IsAuthenticated)
            {
                StatusMessage = "Opening Google sign-in...";
                await _accountService.SignInWithGoogleAsync();
            }

            StatusMessage = "Account ready. Let's connect your robot.";
            RemoteSetupRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Sign-in was cancelled. You can try again whenever you're ready.";
        }
        catch
        {
            StatusMessage = "We couldn't sign you in. Check your internet connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}