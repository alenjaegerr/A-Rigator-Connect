using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace A_Rigator_Controller.ViewModels;

public sealed class RemoteWifiSetupViewModel : INotifyPropertyChanged
{
    private string _statusMessage =
        "Remote cloud access is not active yet. A-Rigator's local IoT Wi-Fi and direct AP modes remain available.";

    public RemoteWifiSetupViewModel()
    {
        NotNowCommand = new Command(() => NotNowRequested?.Invoke(this, EventArgs.Empty));
        ContinueCommand = new Command(() => DashboardRequested?.Invoke(this, EventArgs.Empty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DashboardRequested;
    public event EventHandler? NotNowRequested;

    public ICommand NotNowCommand { get; }
    public ICommand ContinueCommand { get; }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}