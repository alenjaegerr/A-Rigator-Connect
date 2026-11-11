using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Text;
using A_Rigator_Controller.Services;
using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.ViewModels;

public sealed class PairingViewModel : INotifyPropertyChanged
{
    private readonly IWifiStatusService _wifiStatusService;
    private readonly IRobotCommunicationService _robotCommunicationService;
    private readonly IRobotProvisioningService _robotProvisioningService;
    private RobotProvisioningDevice? _selectedBleRobot;
    private string _bleStatusMessage = "Use Bluetooth to discover a nearby A-Rigator (BLE discovery prototype).";
    private PairingStage _stage = PairingStage.DiscoverRobot;
    private string _robotAddress = string.Empty;
    private string _errorMessage = string.Empty;
    private string _resultMessage = string.Empty;
    private bool _isConnectionVerified;
    private bool _isWifiEnabled;
    private bool _isBusy;
    private bool _isBleConnected;
    private string _wifiScanStatusMessage = "Connect to A-Rigator, then scan for nearby Wi-Fi networks.";
    private bool _hasScannedWifiNetworks;
    private RobotWifiNetwork? _selectedWifiNetwork;
    private string _wifiPassword = string.Empty;
    private string _accessPointPassword = string.Empty;
    private string _newAccessPointPassword = string.Empty;
    private string _accessPointStatusMessage = "Pair with A-Rigator over Bluetooth to retrieve its private hotspot password.";
    private string _mqttUsername = string.Empty;
    private string _mqttPassword = string.Empty;
    private string _mqttProvisioningStatusMessage = "Enter the EMQX device username and password to save them over BLE.";

    public PairingViewModel(IWifiStatusService wifiStatusService, IRobotCommunicationService robotCommunicationService, IRobotProvisioningService robotProvisioningService)
    {
        _wifiStatusService = wifiStatusService;
        _robotCommunicationService = robotCommunicationService;
        _robotProvisioningService = robotProvisioningService;
        OpenWifiSettingsCommand = new Command(OpenWifiSettings);
        StartDirectWifiSetupCommand = new Command(StartDirectWifiSetup);
        ContinueToVerifyCommand = new Command(() => SetStage(PairingStage.VerifyConnection));
        BackToWifiSetupCommand = new Command(() => SetStage(PairingStage.ConfigureWifi));
        BackToPhoneNetworkCommand = new Command(() => SetStage(PairingStage.PhoneNetwork));
        ConfirmWifiConnectionCommand = new Command(async () => await ConfirmWifiConnectionAsync());
        OpenDashboardPreviewCommand = new Command(OpenDashboardPreview);
        OpenRemoteWifiSetupCommand = new Command(OpenRemoteWifiSetup);
        NotNowCommand = new Command(NotNow);
        StartOverCommand = new Command(StartOver);
        ScanForBleRobotsCommand = new Command(async () => await ScanForBleRobotsAsync());
        ConnectToBleRobotCommand = new Command(async () => await ConnectToBleRobotAsync());
        DisconnectBleRobotCommand = new Command(async () => await DisconnectBleRobotAsync());
        ScanWifiNetworksCommand = new Command(async () => await ScanWifiNetworksAsync());
        ConnectRobotToWifiCommand = new Command(async () => await ConnectRobotToWifiAsync());
        ChangeAccessPointPasswordCommand = new Command(async () => await ChangeAccessPointPasswordAsync());
        SaveMqttCredentialsCommand = new Command(async () => await SaveMqttCredentialsAsync());
        RefreshWifiState();
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
                OnPropertyChanged(nameof(CanSubmitMqttCredentials));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string RobotAccessPointSsid => RobotNetworkOptions.AccessPointSsid;

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
    public string AccessPointPassword
    {
        get => _accessPointPassword;
        private set => SetField(ref _accessPointPassword, value);
    }
    public string NewAccessPointPassword
    {
        get => _newAccessPointPassword;
        set => SetField(ref _newAccessPointPassword, value);
    }
    public string AccessPointStatusMessage
    {
        get => _accessPointStatusMessage;
        private set => SetField(ref _accessPointStatusMessage, value);
    }
    public ICommand ChangeAccessPointPasswordCommand { get; }
    public ICommand SaveMqttCredentialsCommand { get; }

    public string MqttUsername
    {
        get => _mqttUsername;
        set
        {
            if (SetField(ref _mqttUsername, value))
                OnPropertyChanged(nameof(CanSubmitMqttCredentials));
        }
    }

    public string MqttPassword
    {
        get => _mqttPassword;
        set
        {
            if (SetField(ref _mqttPassword, value))
                OnPropertyChanged(nameof(CanSubmitMqttCredentials));
        }
    }

    public string MqttProvisioningStatusMessage
    {
        get => _mqttProvisioningStatusMessage;
        private set => SetField(ref _mqttProvisioningStatusMessage, value);
    }

    public bool CanSubmitMqttCredentials => IsBleConnected && !IsBusy &&
        !string.IsNullOrWhiteSpace(MqttUsername) && !string.IsNullOrEmpty(MqttPassword);
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

    public bool IsBleConnected
    {
        get => _isBleConnected;
        private set
        {
            if (SetField(ref _isBleConnected, value))
            {
                OnPropertyChanged(nameof(CanSubmitWifiCredentials));
                OnPropertyChanged(nameof(CanSubmitMqttCredentials));
                OnPropertyChanged(nameof(IsWifiInstructionsVisible));
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

    public ICommand OpenWifiSettingsCommand { get; }

    public ICommand ConfirmWifiConnectionCommand { get; }

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

    public string WifiStatusMessage => _isWifiEnabled
        ? "Wi-Fi is on. Connect to the A-Rigator network in Android settings, then return here to verify the robot."
        : "Wi-Fi appears to be off. Turn it on in Android Wi-Fi settings, connect to the A-Rigator network, then return here.";

    public bool IsWifiSettingsButtonVisible => true;

    public string SuccessfulResultMessage => "A-Rigator has joined your Wi-Fi network. Connect this phone to the same network to control it.";

    public bool IsErrorVisible => !string.IsNullOrWhiteSpace(ResultMessage);

    public bool IsSuccessVisible => IsConnectionVerified;

    public bool IsDiscoverStage => _stage == PairingStage.DiscoverRobot;

    public bool IsWifiSetupStage => _stage == PairingStage.ConfigureWifi;

    public bool IsPhoneNetworkStage => _stage == PairingStage.PhoneNetwork;

    public bool IsVerifyStage => _stage == PairingStage.VerifyConnection;

    public bool IsWifiInstructionsVisible => _stage == PairingStage.DirectWifi;

    public bool IsResultVisible => _stage == PairingStage.Result;

    public string RobotAddress
    {
        get => _robotAddress;
        private set => SetField(ref _robotAddress, value);
    }

    public string SelectedNetworkName => SelectedWifiNetwork?.Ssid ?? "your Wi-Fi network";

    public string SetupStepLabel => _stage switch
    {
        PairingStage.DiscoverRobot => "STEP 1 OF 4 · FIND YOUR ROBOT",
        PairingStage.ConfigureWifi => "STEP 2 OF 4 · CONNECT TO WI-FI",
        PairingStage.PhoneNetwork => "STEP 3 OF 4 · CONNECT YOUR PHONE",
        PairingStage.VerifyConnection => "STEP 4 OF 4 · VERIFY CONNECTION",
        PairingStage.Result => "SETUP COMPLETE",
        _ => "DIRECT CONNECTION SETUP"
    };

    public double SetupProgress => _stage switch
    {
        PairingStage.DiscoverRobot => 0.25,
        PairingStage.ConfigureWifi => 0.50,
        PairingStage.PhoneNetwork => 0.75,
        PairingStage.VerifyConnection => 0.90,
        PairingStage.Result => 1.0,
        _ => 0.25
    };

    public ICommand StartDirectWifiSetupCommand { get; }
    public ICommand ContinueToVerifyCommand { get; }
    public ICommand BackToWifiSetupCommand { get; }
    public ICommand BackToPhoneNetworkCommand { get; }

    public void RefreshWifiState()
    {
        var isWifiEnabled = _wifiStatusService.IsWifiEnabled;
        if (_isWifiEnabled == isWifiEnabled)
        {
            return;
        }

        _isWifiEnabled = isWifiEnabled;
        OnPropertyChanged(nameof(WifiStatusMessage));
    }

    public void StartNewPairingFlow()
    {
        ErrorMessage = string.Empty;
        ResultMessage = string.Empty;
        RobotAddress = string.Empty;
        AccessPointPassword = string.Empty;
        NewAccessPointPassword = string.Empty;
        AccessPointStatusMessage = "Pair with A-Rigator over Bluetooth to retrieve its private hotspot password.";
        IsConnectionVerified = false;
        RefreshWifiState();
        SetStage(PairingStage.DiscoverRobot);
    }

    private async Task ConfirmWifiConnectionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        RefreshWifiState();
        if (!_isWifiEnabled)
        {
            ErrorMessage = "Turn on Wi-Fi in Android settings before confirming this setup step.";
            return;
        }

        ErrorMessage = string.Empty;
        ResultMessage = string.Empty;
        IsBusy = true;
        try
        {
            await _robotCommunicationService.ConnectAsync();
            var status = await _robotCommunicationService.GetStatusAsync();
            if (status is null)
            {
                await _robotCommunicationService.DisconnectAsync();
                ResultMessage = "The device responded, but its status was invalid. Check that the ESP32 firmware matches the app protocol.";
                return;
            }

            IsConnectionVerified = true;
            SetStage(PairingStage.Result);
        }
        catch (Exception)
        {
            ResultMessage = $"Could not reach A-Rigator over the router{(string.IsNullOrWhiteSpace(RobotAddress) ? string.Empty : $" at {RobotAddress}")}. Your network may block local device traffic. Try the direct A-Rigator hotspot instead.";
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
        BleStatusMessage = "Scanning for A-Rigator over Bluetooth...";
        try
        {
            var devices = await _robotProvisioningService.ScanForRobotsAsync();
            foreach (var device in devices)
                NearbyBleRobots.Add(device);

            if (devices.Count == 1)
                SelectedBleRobot = devices[0];

            BleStatusMessage = devices.Count == 0
                ? "No robot found. Check that A-Rigator is powered on, then scan again."
                : devices.Count == 1
                    ? "A-Rigator found. Select the device card, then tap Connect."
                    : $"{devices.Count} robots found. Select the one you want to connect to.";
        }
        catch (Exception ex)
        {
            BleStatusMessage = $"Bluetooth discovery failed: {ex.Message}";
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
            try
            {
                AccessPointPassword = await _robotProvisioningService.GetAccessPointPasswordAsync();
                AccessPointStatusMessage = "Hotspot password received over the encrypted Bluetooth link. BLE Just Works does not protect against an active man-in-the-middle.";
            }
            catch (Exception ex)
            {
                AccessPointStatusMessage = $"Could not retrieve hotspot credentials: {ex.Message}";
            }
            SetStage(PairingStage.ConfigureWifi);
            WifiScanStatusMessage = "Bluetooth is ready. Scan to see Wi-Fi networks detected by the robot.";
            HasScannedWifiNetworks = false;
            NearbyWifiNetworks.Clear();
        }
        catch (Exception ex)
        {
            BleStatusMessage = $"Could not connect over Bluetooth: {ex.Message}";
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
            WifiScanStatusMessage = "Connect to A-Rigator, then scan for nearby Wi-Fi networks.";
        }
        catch (Exception ex)
        {
            BleStatusMessage = $"Could not disconnect: {ex.Message}";
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
        WifiScanStatusMessage = "Scanning for nearby Wi-Fi networks...";
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
            WifiScanStatusMessage = $"Wi-Fi scan failed: {ex.Message}";
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
        WifiScanStatusMessage = $"Sending Wi-Fi settings for {SelectedWifiNetwork.Ssid}...";
        try
        {
            var result = await _robotProvisioningService.SendWifiCredentialsAsync(
                SelectedWifiNetwork.Ssid,
                WifiPassword);

            WifiScanStatusMessage = result.Message;
            if (result.Success)
            {
                if (!string.IsNullOrWhiteSpace(result.DeviceAddress))
                {
                    RobotAddress = result.DeviceAddress;
                    _robotCommunicationService.SetRobotAddress(result.DeviceAddress);
                }
                WifiPassword = string.Empty;
                BleStatusMessage = "A-Rigator joined the selected Wi-Fi network. Its direct hotspot remains enabled.";
                SetStage(PairingStage.PhoneNetwork);
            }
        }
        catch (Exception ex)
        {
            WifiScanStatusMessage = $"Could not provision Wi-Fi: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveMqttCredentialsAsync()
    {
        if (IsBusy || !IsBleConnected)
        {
            MqttProvisioningStatusMessage = "Connect to A-Rigator over Bluetooth before saving MQTT credentials.";
            return;
        }

        if (string.IsNullOrWhiteSpace(MqttUsername) || string.IsNullOrEmpty(MqttPassword))
        {
            MqttProvisioningStatusMessage = "Enter both the EMQX username and password.";
            return;
        }

        IsBusy = true;
        MqttProvisioningStatusMessage = "Sending MQTT credentials to A-Rigator over encrypted BLE...";
        try
        {
            var result = await _robotProvisioningService.SendMqttCredentialsAsync(
                MqttUsername.Trim(),
                MqttPassword);

            MqttProvisioningStatusMessage = result.Message;
            if (result.Success)
                MqttPassword = string.Empty;
        }
        catch (Exception ex)
        {
            MqttProvisioningStatusMessage = $"Could not save MQTT credentials: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ChangeAccessPointPasswordAsync()
    {
        if (IsBusy || !IsBleConnected)
        {
            AccessPointStatusMessage = "Reconnect to A-Rigator over Bluetooth before changing its hotspot password.";
            return;
        }

        if (Encoding.UTF8.GetByteCount(NewAccessPointPassword) is < 8 or > 63)
        {
            AccessPointStatusMessage = "Use a password between 8 and 63 UTF-8 bytes.";
            return;
        }

        IsBusy = true;
        AccessPointStatusMessage = "Updating the hotspot password and restarting its access point...";
        try
        {
            var result = await _robotProvisioningService.SetAccessPointPasswordAsync(NewAccessPointPassword);
            AccessPointStatusMessage = result.Message;
            if (result.Success)
            {
                AccessPointPassword = NewAccessPointPassword;
                NewAccessPointPassword = string.Empty;
            }
        }
        catch (Exception ex)
        {
            AccessPointStatusMessage = $"Could not update hotspot password: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenWifiSettings()
    {
        try
        {
            _wifiStatusService.OpenWifiSettings();
            ErrorMessage = string.Empty;
        }
        catch (Exception)
        {
            ErrorMessage = "Could not open Wi-Fi settings. Open them manually and try again.";
        }
    }

    private void StartOver()
    {
        StartNewPairingFlow();
    }

    private void StartDirectWifiSetup()
    {
        ErrorMessage = string.Empty;
        ResultMessage = string.Empty;
        if (!IsBleConnected || string.IsNullOrWhiteSpace(AccessPointPassword))
        {
            ErrorMessage = "Pair with A-Rigator over Bluetooth first. The robot uses a unique hotspot password, which the app retrieves after pairing.";
            SetStage(PairingStage.DiscoverRobot);
            return;
        }
        RobotAddress = "192.168.4.1";
        _robotCommunicationService.SetRobotAddress(RobotAddress);
        SetStage(PairingStage.DirectWifi);
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
        OnPropertyChanged(nameof(IsWifiInstructionsVisible));
        OnPropertyChanged(nameof(IsDiscoverStage));
        OnPropertyChanged(nameof(IsWifiSetupStage));
        OnPropertyChanged(nameof(IsPhoneNetworkStage));
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
        PhoneNetwork,
        VerifyConnection,
        DirectWifi,
        Result
    }
}
