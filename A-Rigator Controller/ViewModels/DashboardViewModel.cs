using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Networking;
using A_Rigator_Controller.Models;
using A_Rigator_Controller.Services;

namespace A_Rigator_Controller.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(2);
    private readonly IRobotCommunicationService _communicationService;
    private readonly IUserProfileService _userProfileService;
    private readonly IWifiSettingsService _wifiSettingsService;
    private readonly IrrigationProfileService _irrigationProfileService;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly SemaphoreSlim _statusLock = new(1, 1);
    private CancellationTokenSource? _monitoringCancellation;
    private Task? _monitoringTask;
    private RobotStatus? _latestStatus;
    private RobotControlCommand? _activeMotorCommand;
    private bool _pumpRequested;
    private string _message = string.Empty;
    private int _reconnectTicks;
    private bool _hasAttemptedConnection;
    private bool _hasEverConnected;
    private bool _isRefreshing;
    private bool _isCommandRunning;
    private bool _isManualTerrainModeActive;

    public DashboardViewModel(
        IRobotCommunicationService communicationService,
        IUserProfileService userProfileService,
        IWifiSettingsService wifiSettingsService,
        IrrigationProfileService irrigationProfileService)
    {
        _communicationService = communicationService;
        _userProfileService = userProfileService;
        _wifiSettingsService = wifiSettingsService;
        _irrigationProfileService = irrigationProfileService;
        _irrigationProfileService.SelectedProfileChanged += OnSelectedIrrigationProfileChanged;
        _communicationService.ConnectionStateChanged += OnConnectionStateChanged;
        Connectivity.ConnectivityChanged += OnConnectivityChanged;

        ForwardCommand = new Command(async () => await SetMotorCommandAsync(RobotControlCommand.Forward), () => CanManualControl);
        ReverseCommand = new Command(async () => await SetMotorCommandAsync(RobotControlCommand.Reverse), () => CanManualControl);
        SteerLeftCommand = new Command(async () => await SetSteeringAsync(RobotControlCommand.SteerLeft), () => CanManualControl);
        SteerRightCommand = new Command(async () => await SetSteeringAsync(RobotControlCommand.SteerRight), () => CanManualControl);
        SteerCenterCommand = new Command(async () => await SetSteeringAsync(RobotControlCommand.SteerCenter), () => CanManualControl);
        StartManualTerrainCommand = new Command(async () => await SetManualTerrainModeAsync(true), () => CanManualControl && !IsManualTerrainModeActive);
        StopManualTerrainCommand = new Command(async () => await SetManualTerrainModeAsync(false), () => CanControl && IsManualTerrainModeActive);
        NozzleSweepCommand = new Command(async () => await SweepNozzleAsync(), () => CanManualControl);
        StartManualIrrigationCommand = new Command(async () => await StartManualIrrigationAsync(), () => CanManualControl && SelectedIrrigationProfile is not null);
        StopManualIrrigationCommand = new Command(async () => await StopManualIrrigationAsync(), () => IsConnected);
        StopCommand = new Command(async () => await StopAsync(), () => CanControl);
        PumpOnCommand = new Command(async () => await SetPumpAsync(true), () => CanManualControl);
        PumpOffCommand = new Command(async () => await SetPumpAsync(false), () => CanControl);
        ResetShieldCommand = new Command(async () => await ResetShieldAsync(), () => CanControl);
        RefreshCommand = new Command(async () => await RefreshStatusAsync(), () => IsConnected);
        StartAutonomousCommand = new Command(async () => await StartAutonomousAsync(), () => CanStartAutonomous);
        StopAutonomousCommand = new Command(async () => await StopAutonomousAsync(), () => CanStopAutonomous);
        StartPathwayCommand = new Command(async () => await StartPathwayAsync(), () => CanStartAutonomous);
        StopPathwayCommand = new Command(async () => await StopPathwayAsync(), () => CanStopAutonomous);
        StartTerrainCommand = new Command(async () => await StartTerrainAsync(), () => CanStartAutonomous);
        TerrainBiasLeftCommand = new Command(async () => await SetTerrainBiasAsync(RobotControlCommand.TerrainBiasLeft), () => CanAdjustTerrainBias);
        TerrainBiasRightCommand = new Command(async () => await SetTerrainBiasAsync(RobotControlCommand.TerrainBiasRight), () => CanAdjustTerrainBias);
        TerrainBiasCenterCommand = new Command(async () => await SetTerrainBiasAsync(RobotControlCommand.TerrainBiasCenter), () => CanAdjustTerrainBias);
        StopTerrainCommand = new Command(async () => await StopTerrainAsync(), () => CanStopTerrain);
        RetryConnectionCommand = new Command(async () => await SearchForRobotAsync(), () => !IsConnected && !IsConnecting);
        ConnectViaApCommand = new Command(async () => await ConnectViaAccessPointAsync(), () => !IsConnected && !IsConnecting);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<IrrigationProfile> IrrigationProfiles => _irrigationProfileService.Profiles;

    public IrrigationProfile? SelectedIrrigationProfile
    {
        get => _irrigationProfileService.SelectedProfile;
        set
        {
            if (value is null || value.Id == _irrigationProfileService.SelectedProfile?.Id) return;
            _irrigationProfileService.SelectProfile(value.Id);
        }
    }

    public string SelectedIrrigationProfileSummary => _irrigationProfileService.SelectedProfile is { } profile
        ? $"{profile.Name} · L {profile.LeftDurationSeconds}s / B {profile.BackDurationSeconds}s / R {profile.RightDurationSeconds}s · {profile.RepeatCount} pass(es)"
        : "No irrigation profile selected";

    public string RobotName => "A-Rigator";

    public string Greeting => $"Hello, {_userProfileService.GetDisplayName()}!";

    public string ConnectionStatus => _communicationService.ConnectionState.ToString().ToUpperInvariant();

    public string RobotStatus => _latestStatus?.State.ToString().ToUpperInvariant() ?? "UNAVAILABLE";

    public string ModeStatus => IsAutonomous
        ? _latestStatus?.AutonomousMode switch
        {
            "TERRAIN" => "TERRAIN MODE",
            "PATHWAY" => "PATHWAY MODE",
            "GENERIC" => "AUTONOMOUS",
            _ => "AUTONOMOUS"
        }
        : _latestStatus?.Mode?.ToString().ToUpperInvariant() ?? "UNAVAILABLE";

    public string PumpStatus => _latestStatus is null
        ? "UNAVAILABLE"
        : _latestStatus.PumpIsOn ? "ON" : "OFF";

    public string MotorStatus => _activeMotorCommand?.ToString().ToUpperInvariant() ?? "STOPPED";

    public string WaterStatus => "UNAVAILABLE";

    public string BatteryStatus => "UNAVAILABLE";

    public string ObstacleDistanceStatus => _latestStatus is
        { ObstacleEchoDetected: true, ObstacleDistanceCm: float distance }
            ? $"{distance:0.0} cm"
            : _latestStatus?.ObstacleEchoDetected == false ? "No echo" : "UNAVAILABLE";

    public string ObstacleStopThresholdStatus => _latestStatus?.ObstacleStopThresholdCm is int threshold
        ? $"{threshold} cm"
        : "UNAVAILABLE";

    public string ManualControlTimeoutStatus => _latestStatus?.ManualControlTimeoutMs is uint timeout
        ? $"{timeout} ms"
        : "UNAVAILABLE";

    public string ObstacleStatus => _latestStatus is null
        ? "UNAVAILABLE"
        : _latestStatus.ObstacleEchoDetected switch
        {
            false => "NO ECHO",
            true when _latestStatus.ObstacleDetected == true => "DETECTED",
            true => "CLEAR",
            _ => "UNAVAILABLE"
        };

    // TCRT5000 digital output: black = HIGH (1), white = LOW (0).
    // Display physical GPIO numbers until left/right sensor placement is verified.
    public string LineGpio14Status => _latestStatus?.LineGpio14 switch
    {
        true => "BLACK",
        false => "WHITE",
        _ => "UNAVAILABLE"
    };

    public string LineGpio27Status => _latestStatus?.LineGpio27 switch
    {
        true => "BLACK",
        false => "WHITE",
        _ => "UNAVAILABLE"
    };

    public string ConnectionModeLabel => _latestStatus?.ConnectionMode switch
    {
        RobotConnectionMode.IoT => "IoT / Router Wi-Fi",
        RobotConnectionMode.AccessPoint => "Direct AP",
        _ => "Unknown / Searching"
    };

    public string ConnectionDetails => _latestStatus is null
        ? "No recent robot telemetry is available."
        : _latestStatus.ConnectionMode == RobotConnectionMode.AccessPoint
            ? (_latestStatus.WifiConnected == true
                ? $"Phone-to-robot link: Direct AP · Robot router link also active ({_latestStatus.WifiIp ?? "IP not reported"})"
                : "Phone-to-robot link: Direct AP · Robot is not connected to a router")
            : _latestStatus.WifiConnected == true
                ? $"Phone-to-robot link: IoT Wi-Fi · {_latestStatus.WifiIp ?? "IP not reported"}"
                : "Robot is not currently connected to its saved router. Its setup AP may still be available.";

    public bool IsApModeMobileDataWarningVisible =>
        _latestStatus?.ConnectionMode == RobotConnectionMode.AccessPoint &&
        Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.Cellular);

    public string ApModeMobileDataWarning =>
        "Mobile data is enabled. Some Android phones restrict or deprioritize internet/mobile-data traffic while connected to a Wi-Fi access point without internet. This can interrupt the direct A-Rigator AP session. Turn mobile data off if the robot connection becomes unstable. IoT mode uses your router or hotspot network and does not require this AP-only workaround.";

    public bool IsDisconnectedNoticeVisible => _hasAttemptedConnection && !IsConnected && !IsConnecting;

    public string ConnectionNoticeTitle => _hasEverConnected
        ? "ROBOT CONNECTION LOST"
        : "ROBOT NOT CONNECTED";

    public string ConnectionRecoveryMessage => _latestStatus?.ConnectionMode switch
    {
        RobotConnectionMode.IoT => "A-Rigator was last reached over IoT Wi-Fi. Search the local network again, or reconnect your phone to the A-Rigator setup hotspot.",
        RobotConnectionMode.AccessPoint => "The direct connection was lost. Reconnect your phone to the A-Rigator Wi-Fi hotspot, then try again.",
        _ => "A-Rigator is not responding. Check that the robot is powered on and that your phone is on the same network, or use the setup AP."
    };

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public bool IsConnected => _communicationService.ConnectionState == ConnectionState.Connected;

    public bool IsConnecting => _communicationService.ConnectionState is ConnectionState.Connecting or ConnectionState.Reconnecting;

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetField(ref _isRefreshing, value);
    }

    public bool IsCommandRunning
    {
        get => _isCommandRunning;
        private set => SetField(ref _isCommandRunning, value);
    }

    public bool CanControl => IsConnected && _latestStatus?.ControlsEnabled == true;

    public bool IsAutonomous => _latestStatus?.Mode == OperatingMode.Automatic;

    public bool CanStartAutonomous => CanControl && !IsAutonomous;

    public bool CanStopAutonomous => CanControl && IsAutonomous;

    public bool IsTerrainModeActive => string.Equals(_latestStatus?.AutonomousMode, "TERRAIN", StringComparison.OrdinalIgnoreCase);

    public bool CanAdjustTerrainBias => CanControl && IsAutonomous && IsTerrainModeActive;

    public bool CanStopTerrain => CanControl && IsAutonomous && IsTerrainModeActive;

    public bool CanManualControl => CanControl && !IsAutonomous;

    public bool IsManualTerrainModeActive
    {
        get => _isManualTerrainModeActive;
        private set => SetField(ref _isManualTerrainModeActive, value);
    }

    public string ManualTerrainStatus => IsManualTerrainModeActive ? "ACTIVE" : "OFF";

    public bool IsControlWarningVisible => !CanControl;

    public string ControlAccessMessage => !IsConnected
        ? "Connect to A-Rigator Wi-Fi to check control availability."
        : _latestStatus is null
            ? "Checking robot control availability..."
            : _latestStatus.ControlsEnabled
                ? string.Empty
                : "Setup Wi-Fi is open. Motor and pump stay disabled until WPA2 is configured in the firmware.";

    public ICommand ForwardCommand { get; }

    public ICommand ReverseCommand { get; }

    public ICommand SteerLeftCommand { get; }

    public ICommand SteerRightCommand { get; }

    public ICommand SteerCenterCommand { get; }

    public ICommand StartManualTerrainCommand { get; }

    public ICommand StopManualTerrainCommand { get; }

    public ICommand NozzleSweepCommand { get; }

    public ICommand StartManualIrrigationCommand { get; }

    public ICommand StopManualIrrigationCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand PumpOnCommand { get; }

    public ICommand PumpOffCommand { get; }

    public ICommand ResetShieldCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand StartAutonomousCommand { get; }

    public ICommand StopAutonomousCommand { get; }

    public ICommand StartPathwayCommand { get; }

    public ICommand StopPathwayCommand { get; }

    public ICommand StartTerrainCommand { get; }
    public ICommand TerrainBiasLeftCommand { get; }
    public ICommand TerrainBiasRightCommand { get; }
    public ICommand TerrainBiasCenterCommand { get; }
    public ICommand StopTerrainCommand { get; }

    public ICommand RetryConnectionCommand { get; }

    public ICommand ConnectViaApCommand { get; }

    public void StartMonitoring()
    {
        if (_monitoringCancellation is not null)
        {
            return;
        }

        _monitoringCancellation = new CancellationTokenSource();
        _monitoringTask = MonitorAsync(_monitoringCancellation.Token);
    }

    public void StopMonitoring()
    {
        var cancellation = _monitoringCancellation;
        if (cancellation is null)
        {
            return;
        }

        _monitoringCancellation = null;
        var shouldStopManualOutputs = !IsAutonomous &&
            (_activeMotorCommand is not null || _pumpRequested);
        cancellation.Cancel();
        _activeMotorCommand = null;
        _pumpRequested = false;
        NotifyControlStateChanged();
        _ = StopOutputsAfterMonitoringAsync(cancellation, shouldStopManualOutputs);
    }

    private async Task StartAutonomousAsync()
    {
        if (!CanStartAutonomous)
        {
            Message = "Connect to A-Rigator before starting autonomous irrigation.";
            return;
        }

        _activeMotorCommand = null;
        _pumpRequested = false;
        Message = "Applying the selected irrigation profile...";
        NotifyControlStateChanged();

        if (!await TryApplySelectedIrrigationProfileAsync())
            return;

        Message = "Sending autonomous start command to A-Rigator...";
        if (await SendCommandAsync(RobotControlCommand.AutonomousStart, CancellationToken.None))
            IsManualTerrainModeActive = false;
        await RefreshStatusAsync();

        if (IsAutonomous)
        {
            Message = "Autonomous irrigation started. A-Rigator is now controlling its own movement and watering.";
        }
    }

    private async Task StopAutonomousAsync()
    {
        if (!CanStopAutonomous)
        {
            return;
        }

        Message = "Stopping autonomous irrigation...";
        NotifyControlStateChanged();

        await SendCommandAsync(RobotControlCommand.AutonomousStop, CancellationToken.None);
        await RefreshStatusAsync();

        if (!IsAutonomous)
        {
            Message = "Autonomous irrigation stopped. Robot outputs are disabled.";
        }
    }

    private async Task StartPathwayAsync()
    {
        if (!CanStartAutonomous)
        {
            Message = "Connect to A-Rigator before starting Pathway Mode.";
            return;
        }

        _activeMotorCommand = null;
        _pumpRequested = false;
        Message = "Applying the selected irrigation profile...";
        NotifyControlStateChanged();
        if (!await TryApplySelectedIrrigationProfileAsync())
            return;

        Message = "Starting Pathway Mode: line-following operation...";
        if (await SendCommandAsync(RobotControlCommand.PathwayStart, CancellationToken.None))
            IsManualTerrainModeActive = false;
        await RefreshStatusAsync();
    }

    private async Task StopPathwayAsync()
    {
        if (!CanStopAutonomous) return;
        Message = "Stopping Pathway Mode...";
        await SendCommandAsync(RobotControlCommand.PathwayStop, CancellationToken.None);
        await RefreshStatusAsync();
    }

    private async Task StartTerrainAsync()
    {
        if (!CanStartAutonomous)
        {
            Message = "Connect to A-Rigator before starting Terrain Mode.";
            return;
        }

        _activeMotorCommand = null;
        _pumpRequested = false;
        Message = "Starting Terrain Mode. A-Rigator will move forward while the steering servo oscillates.";
        NotifyControlStateChanged();
        if (await SendCommandAsync(RobotControlCommand.TerrainStart, CancellationToken.None))
            IsManualTerrainModeActive = false;
        await RefreshStatusAsync();
        NotifyControlStateChanged();
    }

    private async Task SetTerrainBiasAsync(RobotControlCommand command)
    {
        if (!CanAdjustTerrainBias) return;
        var direction = command switch
        {
            RobotControlCommand.TerrainBiasLeft => "left",
            RobotControlCommand.TerrainBiasRight => "right",
            _ => "neutral"
        };
        Message = $"Terrain steering bias set to {direction}. Twist movement continues.";
        await SendCommandAsync(command, CancellationToken.None);
    }

    private async Task StopTerrainAsync()
    {
        if (!CanStopTerrain) return;
        Message = "Stopping Terrain Mode...";
        await SendCommandAsync(RobotControlCommand.TerrainStop, CancellationToken.None);
        await RefreshStatusAsync();
        NotifyControlStateChanged();
    }

    private async Task SetMotorCommandAsync(RobotControlCommand command)
    {
        if (!CanManualControl)
        {
            Message = "Drive controls are available only while connected in Manual mode.";
            return;
        }

        _activeMotorCommand = command;
        Message = command == RobotControlCommand.Forward
            ? "Forward command active. Press STOP to halt."
            : "Reverse command active. Press STOP to halt.";
        NotifyControlStateChanged();
        await SendCommandAsync(command, CancellationToken.None);
    }

    private async Task SetManualTerrainModeAsync(bool enable)
    {
        if (enable && !CanManualControl) return;
        if (!enable && !CanControl) return;

        var command = enable ? RobotControlCommand.ManualTerrainStart : RobotControlCommand.ManualTerrainStop;
        Message = enable
            ? "Manual Terrain Mode enabled. Steering alternates between calibrated left and right; drive remains manual."
            : "Manual Terrain Mode disabled. Steering returned to center.";

        if (await SendCommandAsync(command, CancellationToken.None))
        {
            IsManualTerrainModeActive = enable;
            NotifyControlStateChanged();
        }
    }

    private async Task SetSteeringAsync(RobotControlCommand command)
    {
        if (!CanManualControl)
        {
            Message = "Steering is available only while connected in Manual mode.";
            return;
        }

        var direction = command switch
        {
            RobotControlCommand.SteerLeft => "maximum calibrated LEFT",
            RobotControlCommand.SteerRight => "maximum calibrated RIGHT",
            _ => "CENTER"
        };
        Message = $"Moving steering to {direction} position...";
        if (await SendCommandAsync(command, CancellationToken.None) && IsManualTerrainModeActive)
        {
            IsManualTerrainModeActive = false;
            NotifyControlStateChanged();
        }
    }

    private async Task SweepNozzleAsync()
    {
        if (!CanManualControl)
        {
            Message = "Nozzle sweep is available only while connected in Manual mode.";
            return;
        }

        Message = "Nozzle sweep started. The robot will remain stopped while the nozzle moves.";
        await SendCommandAsync(RobotControlCommand.NozzleSweep, CancellationToken.None);
    }

    private async Task StartManualIrrigationAsync()
    {
        var profile = _irrigationProfileService.SelectedProfile;
        if (!CanManualControl || profile is null)
        {
            Message = "Select an irrigation profile and connect in Manual mode first.";
            return;
        }

        Message = $"Applying '{profile.Name}' and starting its manual sweep...";
        if (!await TryApplySelectedIrrigationProfileAsync()) return;

        if (await SendCommandAsync(RobotControlCommand.ManualIrrigationStart, CancellationToken.None))
        {
            _pumpRequested = true;
            Message = $"Manual irrigation started: {profile.DirectionMode}, {profile.DurationSeconds}s per selected direction, {profile.RepeatCount} pass(es).";
            NotifyControlStateChanged();
        }
    }

    private async Task StopManualIrrigationAsync()
    {
        if (!IsConnected) return;
        Message = "Stopping manual irrigation...";
        await SendCommandAsync(RobotControlCommand.ManualIrrigationStop, CancellationToken.None);
        _pumpRequested = false;
        NotifyControlStateChanged();
    }

    private async Task SetPumpAsync(bool enable)
    {
        if (enable && !CanManualControl)
        {
            Message = "Pump activation is available only while connected in Manual mode.";
            return;
        }

        _pumpRequested = enable;
        Message = enable ? "Pump command active. Press PUMP OFF to stop it." : "Pump off command sent.";
        NotifyControlStateChanged();
        await SendCommandAsync(enable ? RobotControlCommand.PumpOn : RobotControlCommand.PumpOff, CancellationToken.None);
    }

    private async Task StopAsync()
    {
        _activeMotorCommand = null;
        _pumpRequested = false;
        Message = "Stop requested. The ESP32 also stops outputs if its manual-control lease expires.";
        NotifyControlStateChanged();
        if (await SendCommandAsync(RobotControlCommand.Stop, CancellationToken.None))
            IsManualTerrainModeActive = false;
        NotifyControlStateChanged();
    }

    private async Task ResetShieldAsync()
    {
        if (!CanControl)
        {
            Message = "Connect to A-Rigator before resetting the motor shield.";
            return;
        }

        _activeMotorCommand = null;
        _pumpRequested = false;
        Message = "Resetting motor shield...";
        NotifyControlStateChanged();
        await SendCommandAsync(RobotControlCommand.ResetShield, CancellationToken.None);
        if (string.IsNullOrEmpty(Message) || Message == "Resetting motor shield...")
        {
            Message = "Motor shield reset command sent.";
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        var statusCounter = 0;
        var statusRefreshTicks = Math.Max(1, (int)(StatusRefreshInterval.TotalMilliseconds / HeartbeatInterval.TotalMilliseconds));
        using var timer = new PeriodicTimer(HeartbeatInterval);

        try
        {
            _hasAttemptedConnection = true;
            await RetryConnectionAsync(cancellationToken, quiet: true);

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!IsConnected)
                {
                    if (++_reconnectTicks >= 20)
                    {
                        _reconnectTicks = 0;
                        await RetryConnectionAsync(cancellationToken, quiet: true);
                    }
                    continue;
                }

                _reconnectTicks = 0;
                var manualIntentActive = _activeMotorCommand is not null || _pumpRequested;
                if ((IsAutonomous || manualIntentActive) && _latestStatus?.ControlsEnabled != false)
                {
                    await SendCommandAsync(RobotControlCommand.Heartbeat, cancellationToken);
                }

                if (++statusCounter >= statusRefreshTicks)
                {
                    statusCounter = 0;
                    await RefreshStatusAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            _activeMotorCommand = null;
            _pumpRequested = false;
            Message = "Connection lost. The ESP32 manual-control timeout should stop manual outputs.";
            NotifyControlStateChanged();
        }
    }

    private async Task RetryConnectionAsync(
        CancellationToken cancellationToken = default,
        bool quiet = false)
    {
        if (IsConnected)
        {
            await RefreshStatusAsync(cancellationToken);
            return;
        }

        _hasAttemptedConnection = true;
        if (!quiet)
            Message = "Searching for A-Rigator on the last known network and setup AP...";

        NotifyConnectionStateChanged();
        try
        {
            await _communicationService.ConnectAsync(cancellationToken);
            _hasEverConnected = true;
            OnPropertyChanged(nameof(ConnectionNoticeTitle));
            if (!quiet)
                Message = "A-Rigator connection restored.";
            await RefreshStatusAsync(cancellationToken);
            if (!IsAutonomous)
                await TryApplySelectedIrrigationProfileAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!quiet)
                Message = "A-Rigator was not found. Check the network or connect directly to its setup AP.";
        }

        NotifyConnectionStateChanged();
    }

    private async Task SearchForRobotAsync()
    {
        // Restore IoT discovery preference after a previous direct-AP attempt.
        if (_latestStatus?.ConnectionMode == RobotConnectionMode.AccessPoint)
        {
            var stationAddress = _latestStatus.WifiConnected == true
                ? _latestStatus.WifiIp
                : null;

            _communicationService.SetRobotAddress(
                string.IsNullOrWhiteSpace(stationAddress) ? "arigator.local" : stationAddress);
        }

        await RetryConnectionAsync();
    }

    private async Task ConnectViaAccessPointAsync()
    {
        try
        {
            _communicationService.SetRobotAddress("192.168.4.1");
            Message = "Connect your phone to the A-Rigator Wi-Fi network in system settings. Return here and tap Search for robot.";
            await _wifiSettingsService.OpenNetworkSettingsAsync();
        }
        catch (Exception ex)
        {
            Message = $"Could not open network settings: {ex.Message}";
        }
    }

    private void OnSelectedIrrigationProfileChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(SelectedIrrigationProfile));
            OnPropertyChanged(nameof(SelectedIrrigationProfileSummary));
            (StartManualIrrigationCommand as Command)?.ChangeCanExecute();
            if (IsConnected && !IsAutonomous)
                _ = TryApplySelectedIrrigationProfileAsync();
            else if (IsAutonomous)
                Message = "Profile selection saved. It will be applied before the next autonomous run.";
        });
    }

    private async Task<bool> TryApplySelectedIrrigationProfileAsync()
    {
        var profile = _irrigationProfileService.SelectedProfile;
        if (profile is null || !IsConnected || IsAutonomous)
            return profile is not null;

        try
        {
            await _communicationService.ApplyIrrigationProfileAsync(profile);
            Message = $"'{profile.Name}' applied to A-Rigator for its next irrigation sequence.";
            return true;
        }
        catch (Exception)
        {
            Message = "Profile saved on this device, but A-Rigator did not confirm it. Reconnect and try selecting the profile again.";
            return false;
        }
    }

    private void NotifyConnectionStateChanged()
    {
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(ConnectionModeLabel));
        OnPropertyChanged(nameof(ConnectionDetails));
        OnPropertyChanged(nameof(IsApModeMobileDataWarningVisible));
        OnPropertyChanged(nameof(ApModeMobileDataWarning));
        OnPropertyChanged(nameof(IsDisconnectedNoticeVisible));
        OnPropertyChanged(nameof(ConnectionNoticeTitle));
        OnPropertyChanged(nameof(ConnectionRecoveryMessage));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(CanControl));
        OnPropertyChanged(nameof(IsControlWarningVisible));
        OnPropertyChanged(nameof(ControlAccessMessage));
        (RetryConnectionCommand as Command)?.ChangeCanExecute();
        (ConnectViaApCommand as Command)?.ChangeCanExecute();
        NotifyControlStateChanged();
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            return;

        await _statusLock.WaitAsync(cancellationToken);
        try
        {
            IsRefreshing = true;
            try
            {
                _latestStatus = await _communicationService.GetStatusAsync(cancellationToken);
                OnPropertyChanged(nameof(RobotStatus));
                OnPropertyChanged(nameof(ModeStatus));
                OnPropertyChanged(nameof(PumpStatus));
                OnPropertyChanged(nameof(ObstacleDistanceStatus));
                OnPropertyChanged(nameof(ObstacleStatus));
                OnPropertyChanged(nameof(ObstacleStopThresholdStatus));
                OnPropertyChanged(nameof(ManualControlTimeoutStatus));
                OnPropertyChanged(nameof(LineGpio14Status));
                OnPropertyChanged(nameof(LineGpio27Status));
                OnPropertyChanged(nameof(ConnectionModeLabel));
                OnPropertyChanged(nameof(ConnectionDetails));
                OnPropertyChanged(nameof(ConnectionRecoveryMessage));
            OnPropertyChanged(nameof(IsApModeMobileDataWarningVisible));
            OnPropertyChanged(nameof(ApModeMobileDataWarning));
                OnPropertyChanged(nameof(IsAutonomous));
                NotifyControlStateChanged();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                if (!IsConnected)
                {
                    _activeMotorCommand = null;
                    _pumpRequested = false;
                    Message = "Robot connection lost. Manual controls are locked while the ESP32 safety timeout handles active outputs.";
                }
                NotifyConnectionStateChanged();
            }
        }
        finally
        {
            IsRefreshing = false;
            _statusLock.Release();
        }
    }

    private async Task<bool> SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            if (command is not RobotControlCommand.Stop and not RobotControlCommand.PumpOff and not RobotControlCommand.Heartbeat)
            {
                Message = "Robot disconnected. The ESP32 timeout should stop manual outputs.";
            }
            return false;
        }

        if (_latestStatus?.ControlsEnabled == false &&
            command is not RobotControlCommand.Stop and not RobotControlCommand.PumpOff and not RobotControlCommand.Heartbeat)
        {
            Message = "Setup Wi-Fi is open. Motor and pump stay disabled until WPA2 is configured in the firmware.";
            return false;
        }

        try
        {
            await _commandLock.WaitAsync(cancellationToken);
            try
            {
                if (command != RobotControlCommand.Heartbeat)
                {
                    IsCommandRunning = true;
                }

                await _communicationService.SendCommandAsync(command, cancellationToken);
            }
            finally
            {
                if (command != RobotControlCommand.Heartbeat)
                    IsCommandRunning = false;
                _commandLock.Release();
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            Message = "Command failed or connection was lost. The ESP32 timeout should stop manual outputs.";
            _activeMotorCommand = null;
            _pumpRequested = false;
            NotifyControlStateChanged();
            return false;
        }
    }

    private async Task StopOutputsAfterMonitoringAsync(
        CancellationTokenSource cancellation,
        bool shouldStopManualOutputs)
    {
        try
        {
            await cancellation.CancelAsync();
            if (shouldStopManualOutputs)
            {
                await SendCommandAsync(RobotControlCommand.Stop, CancellationToken.None);
            }
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void OnConnectionStateChanged(ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(ConnectionStatus));
            OnPropertyChanged(nameof(ConnectionModeLabel));
            OnPropertyChanged(nameof(ConnectionDetails));
            OnPropertyChanged(nameof(IsDisconnectedNoticeVisible));
            OnPropertyChanged(nameof(ConnectionRecoveryMessage));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsConnecting));
                OnPropertyChanged(nameof(CanControl));
            OnPropertyChanged(nameof(IsControlWarningVisible));
            OnPropertyChanged(nameof(ControlAccessMessage));
            NotifyControlStateChanged();
        });
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(IsApModeMobileDataWarningVisible));
            OnPropertyChanged(nameof(ApModeMobileDataWarning));
        });
    }

    private void NotifyControlStateChanged()
    {
        OnPropertyChanged(nameof(MotorStatus));
        OnPropertyChanged(nameof(IsAutonomous));
        OnPropertyChanged(nameof(CanStartAutonomous));
        OnPropertyChanged(nameof(CanStopAutonomous));
        OnPropertyChanged(nameof(IsTerrainModeActive));
        OnPropertyChanged(nameof(CanAdjustTerrainBias));
        OnPropertyChanged(nameof(CanStopTerrain));
        OnPropertyChanged(nameof(CanManualControl));
        OnPropertyChanged(nameof(IsManualTerrainModeActive));
        OnPropertyChanged(nameof(ManualTerrainStatus));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanControl));
        OnPropertyChanged(nameof(IsControlWarningVisible));
        OnPropertyChanged(nameof(ControlAccessMessage));
        (ForwardCommand as Command)?.ChangeCanExecute();
        (ReverseCommand as Command)?.ChangeCanExecute();
        (SteerLeftCommand as Command)?.ChangeCanExecute();
        (SteerRightCommand as Command)?.ChangeCanExecute();
        (SteerCenterCommand as Command)?.ChangeCanExecute();
        (StartManualTerrainCommand as Command)?.ChangeCanExecute();
        (StopManualTerrainCommand as Command)?.ChangeCanExecute();
        (NozzleSweepCommand as Command)?.ChangeCanExecute();
        (StartManualIrrigationCommand as Command)?.ChangeCanExecute();
        (StopManualIrrigationCommand as Command)?.ChangeCanExecute();
        (StopCommand as Command)?.ChangeCanExecute();
        (PumpOnCommand as Command)?.ChangeCanExecute();
        (PumpOffCommand as Command)?.ChangeCanExecute();
        (ResetShieldCommand as Command)?.ChangeCanExecute();
        (RefreshCommand as Command)?.ChangeCanExecute();
        (StartAutonomousCommand as Command)?.ChangeCanExecute();
        (StopAutonomousCommand as Command)?.ChangeCanExecute();
        (StartPathwayCommand as Command)?.ChangeCanExecute();
        (StopPathwayCommand as Command)?.ChangeCanExecute();
        (StartTerrainCommand as Command)?.ChangeCanExecute();
        (TerrainBiasLeftCommand as Command)?.ChangeCanExecute();
        (TerrainBiasRightCommand as Command)?.ChangeCanExecute();
        (TerrainBiasCenterCommand as Command)?.ChangeCanExecute();
        (StopTerrainCommand as Command)?.ChangeCanExecute();
        (RetryConnectionCommand as Command)?.ChangeCanExecute();
        (ConnectViaApCommand as Command)?.ChangeCanExecute();
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
}
