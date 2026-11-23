using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Text;
using Microsoft.Maui.Storage;
using A_Rigator_Controller.Services;
using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.ViewModels;

public sealed class PairingViewModel : INotifyPropertyChanged
{
    private readonly IRobotCommunicationService _robotCommunicationService;
    private readonly IRobotProvisioningService _robotProvisioningService;
    private readonly IRobotCloudService _robotCloudService;
    private readonly IAccountService _accountService;
    private RobotProvisioningDevice? _selectedBleRobot;
    private string _bleStatusMessage = "Use Bluetooth to discover and connect to your A-Rigator.";
    private PairingStage _stage = PairingStage.DiscoverRobot;
    private string _errorMessage = string.Empty;
    private string _resultMessage = string.Empty;
    private bool _isConnectionVerified;
    private bool _isBusy;
    private bool _isBleConnected;
    private bool _isRemoteSetup;
    private string _wifiScanStatusMessage = "Pair with A-Rigator over Bluetooth, then scan for router networks.";
    private bool _hasScannedWifiNetworks;
    private RobotWifiNetwork? _selectedWifiNetwork;
    private string _wifiPassword = string.Empty;

    public PairingViewModel(IRobotCommunicationService robotCommunicationService, IRobotProvisioningService robotProvisioningService, IRobotCloudService robotCloudService, IAccountService accountService)
    {
        _robotCommunicationService = robotCommunicationService;
        _robotProvisioningService = robotProvisioningService;
        _robotCloudService = robotCloudService;
        _accountService = accountService;
        BackToWifiSetupCommand = new Command(() => SetStage(PairingStage.ConfigureWifi));
        ConfirmWifiConnectionCommand = new Command(async () => await ConfirmWifiConnectionAsync());
        ContinueWithBluetoothCommand = new Command(() =>
        {
            WifiPassword = string.Empty;
            _robotCommunicationService.PreferredConnectionMode = RobotConnectionMode.BLE;
            SetStage(PairingStage.VerifyConnection);
        }, () => IsBleConnected && !IsBusy);
        OpenDashboardPreviewCommand = new Command(OpenDashboardPreview);
        OpenRemoteWifiSetupCommand = new Command(OpenRemoteWifiSetup);
        NotNowCommand = new Command(NotNow);
        StartOverCommand = new Command(StartOver);
        ScanForBleRobotsCommand = new Command(async () => await ScanForBleRobotsAsync());
        ConnectToBleRobotCommand = new Command(async () => await ConnectToBleRobotAsync());
        DisconnectBleRobotCommand = new Command(async () => await DisconnectBleRobotAsync());
        ScanWifiNetworksCommand = new Command(async () => await ScanWifiNetworksAsync());
        ConnectRobotToWifiCommand = new Command(async () => await ConnectRobotToWifiAsync());
    }

    public string ResultMessage
    {
        get => _resultMessage;
        private set
        {
            if (SetField(ref _resultMessage, value))
            {
                OnPropertyChanged(nameof(IsErrorVisible));
            }
        }
    }

    public bool IsConnectionVerified
    {
        get => _isConnectionVerified;
        private set => SetField(ref _isConnectionVerified, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(HasSelectedBleRobot));
                OnPropertyChanged(nameof(CanSubmitWifiCredentials));
                (ContinueWithBluetoothCommand as Command)?.ChangeCanExecute();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public ObservableCollection<RobotProvisioningDevice> NearbyBleRobots { get; } = new();

    public ObservableCollection<RobotWifiNetwork> NearbyWifiNetworks { get; } = new();

    public string WifiScanStatusMessage
    {
        get => _wifiScanStatusMessage;
        private set => SetField(ref _wifiScanStatusMessage, value);
    }

    public bool HasScannedWifiNetworks
    {
        get => _hasScannedWifiNetworks;
        private set => SetField(ref _hasScannedWifiNetworks, value);
    }

    public RobotWifiNetwork? SelectedWifiNetwork
    {
        get => _selectedWifiNetwork;
        set
        {
            if (SetField(ref _selectedWifiNetwork, value))
            {
                OnPropertyChanged(nameof(CanSubmitWifiCredentials));
                OnPropertyChanged(nameof(SelectedNetworkName));
            }
        }
    }

    public string WifiPassword { get => _wifiPassword; set => SetField(ref _wifiPassword, value); }
    public bool CanSubmitWifiCredentials => IsBleConnected && SelectedWifiNetwork is not null && !IsBusy;

    public RobotProvisioningDevice? SelectedBleRobot
    {
        get => _selectedBleRobot;
        set
        {
            if (SetField(ref _selectedBleRobot, value))
            {
                OnPropertyChanged(nameof(HasSelectedBleRobot));
            }
        }
    }

    public bool HasSelectedBleRobot => SelectedBleRobot is not null && !IsBusy;

    public bool IsRemoteSetup => _isRemoteSetup;

    public bool IsBleConnected
    {
        get => _isBleConnected;
        private set
        {
            if (SetField(ref _isBleConnected, value))
            {
                OnPropertyChanged(nameof(CanSubmitWifiCredentials));
                        (ContinueWithBluetoothCommand as Command)?.ChangeCanExecute();
            }
        }
    }

    public string BleStatusMessage
    {
        get => _bleStatusMessage;
        private set => SetField(ref _bleStatusMessage, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? DashboardPreviewRequested;

    public event EventHandler? RemoteWifiSetupRequested;

    public event EventHandler? NotNowRequested;


    public ICommand ConfirmWifiConnectionCommand { get; }


    public ICommand ContinueWithBluetoothCommand { get; }

    public ICommand OpenDashboardPreviewCommand { get; }

    public ICommand OpenRemoteWifiSetupCommand { get; }

    public ICommand NotNowCommand { get; }

    public ICommand StartOverCommand { get; }

    public ICommand ScanForBleRobotsCommand { get; }

    public ICommand ConnectToBleRobotCommand { get; }

    public ICommand DisconnectBleRobotCommand { get; }

    public ICommand ScanWifiNetworksCommand { get; }
    public ICommand ConnectRobotToWifiCommand { get; }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public string SuccessfulResultMessage => "A-Rigator is ready. Choose how you'd like to connect.";

    public bool IsErrorVisible => !string.IsNullOrWhiteSpace(ResultMessage);

    public bool IsSuccessVisible => IsConnectionVerified;

    public bool IsDiscoverStage => _stage == PairingStage.DiscoverRobot;

    public bool IsWifiSetupStage => _stage == PairingStage.ConfigureWifi;


    public bool IsVerifyStage => _stage == PairingStage.VerifyConnection;


    public bool IsResultVisible => _stage == PairingStage.Result;

    public string SelectedNetworkName => SelectedWifiNetwork?.Ssid ?? "your Wi-Fi network";

    public string SetupStepLabel => _stage switch
    {
        PairingStage.DiscoverRobot => "STEP 1 OF 3 · FIND YOUR ROBOT",
        PairingStage.ConfigureWifi => "STEP 2 OF 3 · CONNECT TO WI-FI",
        PairingStage.VerifyConnection => "STEP 3 · VERIFY ROBOT CONNECTION",
        PairingStage.Result => "SETUP COMPLETE",
        _ => "DIRECT CONNECTION SETUP"
    };

    public double SetupProgress => _stage switch
    {
        PairingStage.DiscoverRobot => 0.33,
        PairingStage.ConfigureWifi => 0.66,
        PairingStage.VerifyConnection => 0.90,
        PairingStage.Result => 1.0,
        _ => 0.25
    };

    public ICommand BackToWifiSetupCommand { get; }

    public void StartNewPairingFlow(bool isRemoteSetup = false)
    {
        _isRemoteSetup = isRemoteSetup;
        OnPropertyChanged(nameof(IsRemoteSetup));
        ErrorMessage = string.Empty;
        ResultMessage = string.Empty;
        IsConnectionVerified = false;
        SetStage(PairingStage.DiscoverRobot);
    }

    private async Task ConfirmWifiConnectionAsync()
    {
        if (IsBusy) return;

        ErrorMessage = string.Empty;
        ResultMessage = string.Empty;
        IsBusy = true;
        try
        {
            // IoT is preferred by the connection manager. If the robot is not
            // reachable through MQTT, its already-paired BLE session is used.
            await _robotCommunicationService.ConnectAsync();
            var status = await _robotCommunicationService.GetStatusAsync();
            if (status is null)
            {
                ResultMessage = _robotCommunicationService.PreferredConnectionMode == RobotConnectionMode.IoT
                    ? "A-Rigator hasn't checked in yet. Make sure it's turned on and connected to Wi-Fi, then try again."
                    : "A-Rigator hasn't responded yet. Move closer to your robot and try again.";
                return;
            }

            IsConnectionVerified = true;
            Preferences.Set("arigator_setup_completed", true);
            ResultMessage = status.ConnectionMode == RobotConnectionMode.IoT
                ? "A-Rigator is connected and ready for remote access."
                : "A-Rigator is connected and ready for local control.";
            SetStage(PairingStage.Result);
        }
        catch (Exception)
        {
            ResultMessage = "We couldn't connect to A-Rigator yet. Check that your robot is turned on and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScanForBleRobotsAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        NearbyBleRobots.Clear();
        SelectedBleRobot = null;
        BleStatusMessage = "Looking for your A-Rigator nearby...";
        try
        {
            var devices = await _robotProvisioningService.ScanForRobotsAsync();
            foreach (var device in devices)
                NearbyBleRobots.Add(device);

            if (devices.Count == 1)
                SelectedBleRobot = devices[0];

            BleStatusMessage = devices.Count == 0
                ? "We couldn't find A-Rigator. Make sure it's turned on and nearby, then try again."
                : devices.Count == 1
                    ? "A-Rigator found. Tap Connect to continue."
                    : $"{devices.Count} robots found. Choose the one you want to connect to.";
        }
        catch (Exception ex)
        {
            BleStatusMessage = "We couldn't find your robot. Check that Bluetooth is on and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectToBleRobotAsync()
    {
        if (IsBusy || SelectedBleRobot is null) return;

        IsBusy = true;
        BleStatusMessage = $"Connecting to {SelectedBleRobot.Name}...";
        try
        {
            await _robotProvisioningService.ConnectToRobotAsync(SelectedBleRobot.DeviceId);
            IsBleConnected = true;
            BleStatusMessage = $"Connected to {SelectedBleRobot.Name}.";

            if (_isRemoteSetup)
            {
                var hardwareId = await _robotProvisioningService.GetConnectedRobotHardwareIdAsync();
                var registration = await _robotCloudService.RegisterRobotAsync(hardwareId, "A-Rigator");
                BleStatusMessage = registration.AlreadyRegistered
                    ? "A-Rigator is already linked to your account."
                    : "A-Rigator has been registered to your account.";
            }
            SetStage(PairingStage.ConfigureWifi);
            WifiScanStatusMessage = "A-Rigator is connected to your phone. Choose the Wi-Fi network it should use, or continue with Local control.";
            HasScannedWifiNetworks = false;
            NearbyWifiNetworks.Clear();
        }
        catch (Exception ex)
        {
            BleStatusMessage = "We couldn't connect to A-Rigator. Make sure it's turned on and nearby, then try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectBleRobotAsync()
    {
        try
        {
            await _robotProvisioningService.DisconnectAsync();
            IsBleConnected = false;
            BleStatusMessage = "Disconnected from A-Rigator.";
            NearbyWifiNetworks.Clear();
            HasScannedWifiNetworks = false;
            WifiScanStatusMessage = "Bluetooth is connected. Scan for the router network you want A-Rigator to use.";
        }
        catch (Exception ex)
        {
            BleStatusMessage = "We couldn't disconnect from A-Rigator. Please try again.";
        }
    }

    private async Task ScanWifiNetworksAsync()
    {
        if (IsBusy || !IsBleConnected) return;

        IsBusy = true;
        HasScannedWifiNetworks = false;
        SelectedWifiNetwork = null;
        WifiPassword = string.Empty;
        NearbyWifiNetworks.Clear();
        WifiScanStatusMessage = "Looking for nearby Wi-Fi networks...";
        try
        {
            var networks = await _robotProvisioningService.ScanWifiNetworksAsync();
            foreach (var network in networks)
                NearbyWifiNetworks.Add(network);

            HasScannedWifiNetworks = true;
            WifiScanStatusMessage = networks.Count == 0
                ? "No Wi-Fi networks were found. Move the robot closer to your router and try again."
                : $"{networks.Count} Wi-Fi network(s) found. Select your network, enter its password, then connect A-Rigator.";
        }
        catch (Exception ex)
        {
            WifiScanStatusMessage = "We couldn't find Wi-Fi networks. Check that A-Rigator is nearby and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }
    private async Task ConnectRobotToWifiAsync()
    {
        if (IsBusy || !IsBleConnected || SelectedWifiNetwork is null) return;
        if (SelectedWifiNetwork.IsSecured && Encoding.UTF8.GetByteCount(WifiPassword) < 8)
        {
            WifiScanStatusMessage = "This network is secured. Enter its Wi-Fi password before connecting.";
            return;
        }
        if (!SelectedWifiNetwork.IsSecured && !string.IsNullOrEmpty(WifiPassword))
        {
            WifiScanStatusMessage = "This network is open. Clear the password field before connecting.";
            return;
        }

        IsBusy = true;
        WifiScanStatusMessage = $"Connecting A-Rigator to {SelectedWifiNetwork.Ssid}...";
        try
        {
            var result = await _robotProvisioningService.SendWifiCredentialsAsync(
                SelectedWifiNetwork.Ssid,
                WifiPassword);

            WifiScanStatusMessage = result.Message;
            if (result.Success)
            {
                WifiPassword = string.Empty;
                BleStatusMessage = "A-Rigator is connected to Wi-Fi. You can still control it nearby.";

                if (_isRemoteSetup)
                {
                    WifiScanStatusMessage = "Wi-Fi is ready. Let's check your remote connection.";
                }

                SetStage(PairingStage.VerifyConnection);
            }
        }
        catch (Exception ex)
        {
            WifiScanStatusMessage = "We couldn't connect A-Rigator to that Wi-Fi network. Check the password and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartOver()
    {
        StartNewPairingFlow();
    }

    private void OpenRemoteWifiSetup()
    {
        if (IsResultVisible)
        {
            RemoteWifiSetupRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void NotNow()
    {
        NotNowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenDashboardPreview()
    {
        if (IsResultVisible)
        {
            DashboardPreviewRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetStage(PairingStage stage)
    {
        if (_stage == stage)
        {
            return;
        }

        _stage = stage;
        OnPropertyChanged(nameof(IsDiscoverStage));
        OnPropertyChanged(nameof(IsWifiSetupStage));
        OnPropertyChanged(nameof(IsVerifyStage));
        OnPropertyChanged(nameof(IsResultVisible));
        OnPropertyChanged(nameof(SetupStepLabel));
        OnPropertyChanged(nameof(SetupProgress));
        OnPropertyChanged(nameof(IsErrorVisible));
        OnPropertyChanged(nameof(IsSuccessVisible));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private enum PairingStage
    {
        DiscoverRobot,
        ConfigureWifi,
        VerifyConnection,
        Result
    }
}
