using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using A_Rigator_Controller.Models;
using A_Rigator_Controller.Services;

namespace A_Rigator_Controller.ViewModels;

public sealed class RemoteWifiSetupViewModel : INotifyPropertyChanged
{
    private readonly IAccountService _accountService;
    private readonly IRobotCommunicationService _robotCommunicationService;
    private readonly DashboardViewModel _dashboardViewModel;
    private string _statusMessage = "Choose how you'd like to connect to A-Rigator.";
    private bool _isBusy;

    public RemoteWifiSetupViewModel(
        IAccountService accountService,
        IRobotCommunicationService robotCommunicationService,
        DashboardViewModel dashboardViewModel)
    {
        _accountService = accountService;
        _robotCommunicationService = robotCommunicationService;
        _dashboardViewModel = dashboardViewModel;

        ChooseLocalCommand = new Command(ChooseLocal, () => !IsBusy);
        ChooseRemoteCommand = new Command(async () => await ChooseRemoteAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RemoteSetupRequested;
    public event EventHandler? NotNowRequested;

    public ICommand ChooseLocalCommand { get; }
    public ICommand ChooseRemoteCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotBusy));
            (ChooseLocalCommand as Command)?.ChangeCanExecute();
            (ChooseRemoteCommand as Command)?.ChangeCanExecute();
        }
    }

    public bool IsNotBusy => !IsBusy;

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

    private async void ChooseLocal()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            StatusMessage = "Connecting to A-Rigator nearby...";
            await _dashboardViewModel.SelectConnectionModeAsync(RobotConnectionMode.BLE);
            NotNowRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ChooseRemoteAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            if (!_accountService.IsAuthenticated)
            {
                StatusMessage = "Please sign in to use remote access.";
                await _accountService.SignInWithGoogleAsync();
            }

            await _dashboardViewModel.SelectConnectionModeAsync(RobotConnectionMode.IoT);

            // A saved remote connection means the robot has already been set up.
            // Otherwise, guide the user through nearby Wi-Fi setup.
            if (await _robotCommunicationService.CheckAvailabilityAsync())
            {
                StatusMessage = "Remote access is ready. Connecting to A-Rigator...";
                NotNowRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                StatusMessage = "Let's connect A-Rigator to your Wi-Fi.";
                RemoteSetupRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Sign-in was cancelled. You can try again whenever you're ready.";
        }
        catch
        {
            StatusMessage = "We couldn't start remote access. Check your internet connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}