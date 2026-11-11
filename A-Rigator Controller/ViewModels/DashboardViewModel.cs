using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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

        ForwardCommand = new Command(async () => await SetMotorCommandAsync(RobotControlCommand.Forward), () => CanManualControl);
        ReverseCommand = new Command(async () => await SetMotorCommandAsync(RobotControlCommand.Reverse), () => CanManualControl);
        SteerLeftCommand = new Command(async () => await SetSteeringAsync(RobotControlCommand.SteerLeft), () => CanManualControl);
        SteerRightCommand = new Command(async () => await SetSteeringAsync(RobotControlCommand.SteerRight), () => CanManualControl);
        SteerCenterCommand = new Command(async () => await SetSteeringAsync(RobotControlCommand.SteerCenter), () => CanManualControl);
        NozzleSweepCommand = new Command(async () => await SweepNozzleAsync(), () => CanManualControl);
        StopCommand = new Command(async () => await StopAsync(), () => CanControl);
        PumpOnCommand = new Command(async () => await SetPumpAsync(true), () => CanManualControl);
        PumpOffCommand = new Command(async () => await SetPumpAsync(false), () => CanControl);
        ResetShieldCommand = new Command(async () => await ResetShieldAsync(), () => CanControl);
        RefreshCommand = new Command(async () => await RefreshStatusAsync(), () => IsConnected);
        StartAutonomousCommand = new Command(async () => await StartAutonomousAsync(), () => CanStartAutonomous);
        StopAutonomousCommand = new Command(async () => await StopAutonomousAsync(), () => CanStopAutonomous);
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

    public string ModeStatus => _latestStatus?.Mode?.ToString().ToUpperInvariant() ?? "UNAVAILABLE";

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

    public bool CanManualControl => CanControl && !IsAutonomous;

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

    public ICommand NozzleSweepCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand PumpOnCommand { get; }

    public ICommand PumpOffCommand { get; }

    public ICommand ResetShieldCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand StartAutonomousCommand { get; }

    public ICommand StopAutonomousCommand { get; }

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
        Message = "Sending autonomous start command to A-Rigator...";
        NotifyControlStateChanged();

        await SendCommandAsync(RobotControlCommand.AutonomousStart, CancellationToken.None);
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
        await SendCommandAsync(command, CancellationToken.None);
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
        await SendCommandAsync(RobotControlCommand.Stop, CancellationToken.None);
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
                if (manualIntentActive && _latestStatus?.ControlsEnabled != false)
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
        });
    }

    private void NotifyConnectionStateChanged()
    {
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(ConnectionModeLabel));
        OnPropertyChanged(nameof(ConnectionDetails));
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

    private async Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            if (command is not RobotControlCommand.Stop and not RobotControlCommand.PumpOff and not RobotControlCommand.Heartbeat)
            {
                Message = "Robot disconnected. The ESP32 timeout should stop manual outputs.";
            }
            return;
        }

        if (_latestStatus?.ControlsEnabled == false &&
            command is not RobotControlCommand.Stop and not RobotControlCommand.PumpOff and not RobotControlCommand.Heartbeat)
        {
            Message = "Setup Wi-Fi is open. Motor and pump stay disabled until WPA2 is configured in the firmware.";
            return;
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            Message = "Command failed or connection was lost. The ESP32 timeout should stop manual outputs.";
            _activeMotorCommand = null;
            _pumpRequested = false;
            NotifyControlStateChanged();
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

    private void NotifyControlStateChanged()
    {
        OnPropertyChanged(nameof(MotorStatus));
        OnPropertyChanged(nameof(IsAutonomous));
        OnPropertyChanged(nameof(CanStartAutonomous));
        OnPropertyChanged(nameof(CanStopAutonomous));
        OnPropertyChanged(nameof(CanManualControl));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanControl));
        OnPropertyChanged(nameof(IsControlWarningVisible));
        OnPropertyChanged(nameof(ControlAccessMessage));
        (ForwardCommand as Command)?.ChangeCanExecute();
        (ReverseCommand as Command)?.ChangeCanExecute();
        (SteerLeftCommand as Command)?.ChangeCanExecute();
        (SteerRightCommand as Command)?.ChangeCanExecute();
        (SteerCenterCommand as Command)?.ChangeCanExecute();
        (NozzleSweepCommand as Command)?.ChangeCanExecute();
        (StopCommand as Command)?.ChangeCanExecute();
        (PumpOnCommand as Command)?.ChangeCanExecute();
        (PumpOffCommand as Command)?.ChangeCanExecute();
        (ResetShieldCommand as Command)?.ChangeCanExecute();
        (RefreshCommand as Command)?.ChangeCanExecute();
        (StartAutonomousCommand as Command)?.ChangeCanExecute();
        (StopAutonomousCommand as Command)?.ChangeCanExecute();
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
