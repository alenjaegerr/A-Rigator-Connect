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
    private string _wifiScanStatusMessage = "Connect to A-Rigator first, then look for nearby Wi-Fi networks.";
    private bool _hasScannedWifiNetworks;
    private RobotWifiNetwork? _selectedWifiNetwork;
    private string _wifiPassword = string.Empty;
    private string? _savedWifiNetwork;
    private string _savedWifiStatusMessage = "Checking saved Wi-Fi settings...";

    public PairingViewModel(IRobotCommunicationService robotCommunicationService, IRobotProvisioningService robotProvisioningService, IRobotCloudService robotCloudService, IAccountService accountService)
    {
        _robotCommunicationService = robotCommunicationService;
        _robotProvisioningService = robotProvisioningService;
        _robotCloudService = robotCloudService;
        _accountService = accountService;
        BackToWifiSetupCommand = new Command(() => SetStage(PairingStage.ConfigureWifi));
        ConfirmWifiConnectionCommand = new Command(async () => await ConfirmWifiConnectionAsync());
        ContinueWithBluetoothCommand = new Command(async () => await ContinueLocallyAsync(), () => IsBleConnected && !IsBusy);
        OpenDashboardPreviewCommand = new Command(OpenDashboardPreview);
        OpenRemoteWifiSetupCommand = new Command(OpenRemoteWifiSetup);
        NotNowCommand = new Command(NotNow);
        StartOverCommand = new Command(StartOver);
        ScanForBleRobotsCommand = new Command(async () => await ScanForBleRobotsAsync());
        ConnectToBleRobotCommand = new Command(async () => await ConnectToBleRobotAsync());
        DisconnectBleRobotCommand = new Command(async () => await DisconnectBleRobotAsync());
        ScanWifiNetworksCommand = new Command(async () => await ScanWifiNetworksAsync());
        ConnectRobotToWifiCommand = new Command(async () => await ConnectRobotToWifiAsync());
        ForgetSavedWifiCommand = new Command(async () => await ForgetSavedWifiAsync());
        KeepSavedWifiCommand = new Command(async () => await KeepSavedWifiAsync(), () => HasSavedWifiNetwork && !IsBusy);
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
    public string? SavedWifiNetwork
    {
        get => _savedWifiNetwork;
        private set
        {
            if (SetField(ref _savedWifiNetwork, value))
                OnPropertyChanged(nameof(HasSavedWifiNetwork));
                (KeepSavedWifiCommand as Command)?.ChangeCanExecute();
        }
    }
    public bool HasSavedWifiNetwork => !string.IsNullOrWhiteSpace(SavedWifiNetwork);
    public string SavedWifiStatusMessage
    {
        get => _savedWifiStatusMessage;
        private set => SetField(ref _savedWifiStatusMessage, value);
    }
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
    public ICommand ForgetSavedWifiCommand { get; }
    public ICommand KeepSavedWifiCommand { get; }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public string SuccessfulResultMessage => !IsRemoteSetup
        ? "A-Rigator is ready. Choose how you'd like to connect."
        : _robotCommunicationService.PreferredConnectionMode == RobotConnectionMode.IoT
            ? "A-Rigator is ready for remote access."
            : "A-Rigator is ready for local control.";

    public string CompletionButtonText => "Continue to Dashboard";

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
        OnPropertyChanged(nameof(SuccessfulResultMessage));
        OnPropertyChanged(nameof(CompletionButtonText));
        ErrorMessage = string.Empty;
        ResultMessage = string.Empty;
        IsConnectionVerified = false;
        SetStage(PairingStage.DiscoverRobot);
    }

    public Task StartRemoteWifiSetupFlowAsync() => BeginDiscoveryFlowAsync(isRemoteSetup: true);

    public async Task BeginDiscoveryFlowAsync(bool isRemoteSetup)
    {
        _isRemoteSetup = isRemoteSetup;
        OnPropertyChanged(nameof(IsRemoteSetup));
        OnPropertyChanged(nameof(SuccessfulResultMessage));
        OnPropertyChanged(nameof(CompletionButtonText));
        ErrorMessage = string.Empty;
        ResultMessage = string.Empty;
        IsConnectionVerified = false;
        IsBleConnected = false;
        SelectedBleRobot = null;
        NearbyBleRobots.Clear();
        NearbyWifiNetworks.Clear();
        HasScannedWifiNetworks = false;
        SavedWifiNetwork = null;
        WifiPassword = string.Empty;

        // Remote setup is verified over MQTT. BLE is only needed for local
        // control and first-time Wi-Fi provisioning; requiring BLE here makes
        // a robot with working Wi-Fi/MQTT appear offline after app reinstall.
        if (isRemoteSetup)
        {
            _robotCommunicationService.PreferredConnectionMode = RobotConnectionMode.IoT;
            SavedWifiStatusMessage = "Checking A-Rigator's remote connection...";
            WifiScanStatusMessage = "Verifying the robot through its cloud connection.";
            SetStage(PairingStage.VerifyConnection);
            await ConfirmWifiConnectionAsync();
            return;
        }

        // Local setup uses BLE discovery. Do not trust a cached BLE device or
        // silently reuse a previous GATT session.
        if (_robotCommunicationService.ConnectionState != ConnectionState.Disconnected)
        {
            try { await _robotCommunicationService.DisconnectAsync(); }
            catch { /* The new discovery attempt will report any real failure. */ }
        }

        _robotCommunicationService.PreferredConnectionMode = isRemoteSetup
            ? RobotConnectionMode.IoT
            : RobotConnectionMode.BLE;

        try { await _robotProvisioningService.DisconnectAsync(); }
        catch { /* Clear any stale provisioning session before scanning. */ }

        SavedWifiStatusMessage = "Checking saved Wi-Fi settings...";
        WifiScanStatusMessage = isRemoteSetup
            ? "Find A-Rigator nearby before setting up remote access."
            : "Find A-Rigator nearby to connect locally.";
        SetStage(PairingStage.DiscoverRobot);
        await ScanForBleRobotsAsync();
    }

    private async Task KeepSavedWifiAsync()
    {
        if (IsBusy) return;

        WifiPassword = string.Empty;
        if (_isRemoteSetup)
        {
            _robotCommunicationService.PreferredConnectionMode = RobotConnectionMode.IoT;
            OnPropertyChanged(nameof(SuccessfulResultMessage));
            SetStage(PairingStage.VerifyConnection);
            await ConfirmWifiConnectionAsync();
            return;
        }

        await ContinueLocallyAsync();
    }

    private async Task ContinueLocallyAsync()
    {
        if (IsBusy) return;

        WifiPassword = string.Empty;
        _robotCommunicationService.PreferredConnectionMode = RobotConnectionMode.BLE;
        OnPropertyChanged(nameof(SuccessfulResultMessage));
        SetStage(PairingStage.VerifyConnection);
        await ConfirmWifiConnectionAsync();
    }

    private async Task ConfirmWifiConnectionAsync()
    {
        if (IsBusy) return;

        ErrorMessage = string.Empty;
        ResultMessage = string.Empty;
        IsBusy = true;
        try
        {
            // The selected connection mode is explicit: Local uses the nearby
            // connection, while Remote uses the saved internet connection.
            await _robotCommunicationService.ConnectAsync();
            var status = await _robotCommunicationService.GetStatusAsync();

            // MQTT subscription can complete just before the ESP32's next
            // telemetry publication. Wait briefly instead of making the user
            // press "Check connection" a second time.
            if (status is null && _robotCommunicationService.PreferredConnectionMode == RobotConnectionMode.IoT)
            {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
                while (status is null && DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(500);
                    status = await _robotCommunicationService.GetStatusAsync();
                }
            }

            if (status is null)
            {
                ResultMessage = _robotCommunicationService.PreferredConnectionMode == RobotConnectionMode.IoT
                    ? "A-Rigator hasn't checked in yet. Check its Wi-Fi connection and try again."
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
        catch (OperationCanceledException)
        {
            ResultMessage = "The connection check was cancelled. Try again when you're ready.";
        }
        catch (Exception ex)
        {
            // Keep the real failure visible during setup instead of hiding
            // broker, subscription, TLS, and configuration errors behind one
            // misleading BLE-style message.
            var detail = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().Name
                : ex.Message;
            ResultMessage = $"Remote connection failed ({ex.GetType().Name}): {detail}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScanForBleRobotsAsync()
    {
        if (IsBusy) return;

        RobotProvisioningDevice? autoConnect = null;
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
            {
                SelectedBleRobot = devices[0];
                autoConnect = devices[0];
                BleStatusMessage = "A-Rigator found. Connecting...";
            }
            else
            {
                BleStatusMessage = devices.Count == 0
                    ? "We couldn't find A-Rigator. Make sure it's turned on and nearby, then try again."
                    : $"{devices.Count} robots found. Choose yours to connect.";
            }
        }
        catch
        {
            BleStatusMessage = "We couldn't find your robot. Check that Bluetooth is on and try again.";
        }
        finally
        {
            IsBusy = false;
        }

        // A single nearby A-Rigator is unambiguous, so connect immediately.
        if (autoConnect is not null)
            await ConnectToBleRobotAsync();
    }

    private async Task ConnectToBleRobotAsync()
    {
        if (IsBusy || SelectedBleRobot is null) return;

        var shouldScanWifi = false;
        IsBusy = true;
        BleStatusMessage = $"Connecting to {SelectedBleRobot.Name}...";
        try
        {
            await _robotProvisioningService.ConnectToRobotAsync(SelectedBleRobot.DeviceId);
            IsBleConnected = true;
            BleStatusMessage = $"Connected to {SelectedBleRobot.Name}.";

            if (_accountService.IsAuthenticated)
            {
                var hardwareId = await _robotProvisioningService.GetConnectedRobotHardwareIdAsync();
                var registration = await _robotCloudService.RegisterRobotAsync(hardwareId, "A-Rigator");
                BleStatusMessage = registration.AlreadyRegistered
                    ? "A-Rigator is already linked to your account."
                    : "A-Rigator has been registered to your account.";
            }

            SetStage(PairingStage.ConfigureWifi);
            try
            {
                SavedWifiNetwork = await _robotProvisioningService.GetSavedWifiNetworkAsync();
                SavedWifiStatusMessage = HasSavedWifiNetwork
                    ? "A-Rigator already has a saved Wi-Fi network. Keep it or choose another."
                    : "No Wi-Fi network saved yet. Choose one below or continue locally.";
            }
            catch
            {
                SavedWifiNetwork = null;
                SavedWifiStatusMessage = "No saved Wi-Fi network was found. You can add one below.";
            }

            WifiScanStatusMessage = "Choose a Wi-Fi network, or continue with Local control.";
            HasScannedWifiNetworks = false;
            NearbyWifiNetworks.Clear();
            shouldScanWifi = !HasSavedWifiNetwork;
        }
        catch
        {
            BleStatusMessage = "We couldn't connect to A-Rigator. Make sure it's turned on and nearby, then try again.";
        }
        finally
        {
            IsBusy = false;
        }

        // For a first-time setup, scan automatically so the user only needs to
        // select their router and enter its password.
        if (shouldScanWifi)
            await ScanWifiNetworksAsync();
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
            WifiScanStatusMessage = "Reconnect to A-Rigator before looking for Wi-Fi networks.";
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

        var verifyConnection = false;
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
                SavedWifiNetwork = SelectedWifiNetwork.Ssid;
                SavedWifiStatusMessage = $"A-Rigator is connected to {SavedWifiNetwork} and has saved this network.";
                BleStatusMessage = "A-Rigator is connected to Wi-Fi. You can still control it nearby.";

                // The ESP32 automatically enters Remote Mode after successfully
                // saving Wi-Fi credentials. Align the app transport with that
                // firmware transition before attempting connection verification.
                _robotCommunicationService.PreferredConnectionMode = RobotConnectionMode.IoT;
                OnPropertyChanged(nameof(SuccessfulResultMessage));
                WifiScanStatusMessage = "Wi-Fi is ready. Checking A-Rigator's remote connection...";

                SetStage(PairingStage.VerifyConnection);
                verifyConnection = true;
            }
        }
        catch
        {
            WifiScanStatusMessage = "We couldn't connect A-Rigator to that Wi-Fi network. Check the password and try again.";
        }
        finally
        {
            IsBusy = false;
        }

        if (verifyConnection)
            await ConfirmWifiConnectionAsync();
    }

    private async Task ForgetSavedWifiAsync()
    {
        if (IsBusy || !IsBleConnected) return;
        var confirmed = await Application.Current!.MainPage!.DisplayAlert(
            "Forget saved Wi-Fi?",
            "A-Rigator will disconnect from its saved router network. Local Bluetooth control will still work. Your robot pairing and irrigation profiles will stay unchanged.",
            "Forget network",
            "Keep network");
        if (!confirmed) return;

        IsBusy = true;
        try
        {
            var result = await _robotProvisioningService.ForgetSavedWifiAsync();
            if (result.Success)
            {
                SavedWifiNetwork = null;
                SavedWifiStatusMessage = "No Wi-Fi network is saved. You can choose one below whenever you're ready.";
            }
            else
            {
                SavedWifiStatusMessage = result.Message;
            }
        }
        catch
        {
            SavedWifiStatusMessage = "We couldn't forget the saved network. Keep A-Rigator nearby and try again.";
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
