using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace A_Rigator_Controller.ViewModels;

public sealed class RemoteWifiSetupViewModel : INotifyPropertyChanged
{
    private string _networkName = "Home Wi‑Fi";
    private string _password = string.Empty;
    private string _statusMessage = "Connect the robot to a nearby Wi‑Fi to enable future remote connection mode. You can skip this for now.";
    private bool _isConnected;

    public RemoteWifiSetupViewModel()
    {
        ConnectCommand = new Command(ConnectToWifi);
        NotNowCommand = new Command(NotNow);
        ContinueCommand = new Command(ContinueToDashboard);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? DashboardRequested;

    public event EventHandler? NotNowRequested;

    public ICommand ConnectCommand { get; }

    public ICommand NotNowCommand { get; }

    public ICommand ContinueCommand { get; }

    public string NetworkName
    {
        get => _networkName;
        set
        {
            if (_networkName == value)
            {
                return;
            }

            _networkName = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (_password == value)
            {
                return;
            }

            _password = value;
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

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConnectedMessageVisible));
        }
    }

    public bool IsConnectedMessageVisible => IsConnected;

    private void ConnectToWifi()
    {
        if (string.IsNullOrWhiteSpace(NetworkName))
        {
            StatusMessage = "Enter the nearby Wi‑Fi name before connecting the robot.";
            return;
        }

        IsConnected = true;
        StatusMessage = "A‑Rigator is successfully connected to the nearby Wi‑Fi. Remote connection mode is ready when you choose it.";
    }

    private void NotNow()
    {
        NotNowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ContinueToDashboard()
    {
        DashboardRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
