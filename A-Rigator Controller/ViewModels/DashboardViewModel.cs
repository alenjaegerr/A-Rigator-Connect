using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using A_Rigator_Controller.Models;
using A_Rigator_Controller.Services;

namespace A_Rigator_Controller.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(250);
    private readonly IRobotCommunicationService _communicationService;
    private readonly IUserProfileService _userProfileService;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private CancellationTokenSource? _monitoringCancellation;
    private Task? _monitoringTask;
    private RobotStatus? _latestStatus;
    private RobotControlCommand? _activeMotorCommand;
    private bool _pumpRequested;
    private bool _autonomousRequested;
    private DateTimeOffset? _lastUpdated;
    private string _message = "Connect to the robot to load live telemetry.";

    public DashboardViewModel(
        IRobotCommunicationService communicationService,
        IUserProfileService userProfileService)
    {
        _communicationService = communicationService;
        _userProfileService = userProfileService;
        _communicationService.ConnectionStateChanged += OnConnectionStateChanged;

        ForwardCommand = new Command(async () => await SetMotorCommandAsync(RobotControlCommand.Forward), () => CanControl);
        ReverseCommand = new Command(async () => await SetMotorCommandAsync(RobotControlCommand.Reverse), () => CanControl);
        StopCommand = new Command(async () => await StopAsync(), () => CanControl);
        PumpOnCommand = new Command(async () => await SetPumpAsync(true), () => CanControl);
        PumpOffCommand = new Command(async () => await SetPumpAsync(false), () => CanControl);
        ResetShieldCommand = new Command(async () => await ResetShieldAsync(), () => CanControl);
        RefreshCommand = new Command(async () => await RefreshStatusAsync(), () => IsConnected);
        StartAutonomousCommand = new Command(async () => await StartAutonomousAsync(), () => CanControl && !_autonomousRequested);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RobotName => "A-Rigator";

    public string Greeting => $"Hello, {_userProfileService.GetDisplayName()}!";

    public string ConnectionStatus => _communicationService.ConnectionState.ToString().ToUpperInvariant();

    public string RobotStatus => _latestStatus?.State.ToString().ToUpperInvariant() ?? "UNAVAILABLE";

    public string ModeStatus => _latestStatus?.Mode?.ToString().ToUpperInvariant() ?? "UNAVAILABLE";

    public string PumpStatus => _latestStatus is null
        ? "UNAVAILABLE"
        : _latestStatus.PumpIsOn ? "ON" : "OFF";

    public string MotorStatus => _activeMotorCommand?.ToString().ToUpperInvariant() ?? "STOPPED";

    public string WaterStatus => "NO SENSOR";

    public string WaterGraphMessage => "Live history is not available yet. The current firmware does not report a tank-level sensor.";

    public string BatteryStatus => "NOT REPORTED";

    public string ObstacleDistanceText => "Not reported";

    public string LineSensorSummary => "Not reported";

    public string ModeSummary => _autonomousRequested
        ? "Autonomous run requested"
        : IsConnected ? "Local robot link" : "Waiting for robot connection";

    public string AutonomousStatusTitle => _autonomousRequested || _latestStatus?.Mode == OperatingMode.Automatic
        ? "Autonomous mode requested"
        : "Ready when connected";

    public string AutonomousStatusDescription => _autonomousRequested || _latestStatus?.Mode == OperatingMode.Automatic
        ? "The robot is expected to follow its line, stop for obstacles, and water detected zones. Verify the physical area is clear."
        : "Start sends the autonomous command to the robot. Confirm the robot and test area are clear first.";

    public string LastUpdatedText => _lastUpdated is null
        ? "Telemetry has not been received"
        : $"Updated {_lastUpdated.Value.ToLocalTime():HH:mm:ss}";

    public Color ConnectionBadgeColor => IsConnected ? Color.FromArgb("#DDF1E2") : Color.FromArgb("#F1E7D9");

    public Color ConnectionDotColor => IsConnected ? Color.FromArgb("#23834C") : Color.FromArgb("#A87525");

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public bool IsConnected => _communicationService.ConnectionState == ConnectionState.Connected;

    public bool CanControl => IsConnected && _latestStatus?.ControlsEnabled == true;

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

    public ICommand StopCommand { get; }

    public ICommand PumpOnCommand { get; }

    public ICommand PumpOffCommand { get; }

    public ICommand ResetShieldCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand StartAutonomousCommand { get; }

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
        cancellation.Cancel();
        _activeMotorCommand = null;
        _pumpRequested = false;
        _autonomousRequested = false;
        NotifyControlStateChanged();
        _ = StopOutputsAfterMonitoringAsync(cancellation);
    }

    private async Task StartAutonomousAsync()
    {
        if (!CanControl)
        {
            Message = "Connect to A-Rigator before starting autonomous mode.";
            return;
        }

        var confirmed = await Shell.Current.DisplayAlert(
            "Start autonomous irrigation?",
            "A-Rigator may move, steer, and activate its water pump automatically. Place it in a clear test area and keep hands clear of moving parts.",
            "Start",
            "Cancel");

        if (!confirmed)
        {
            Message = "Autonomous start cancelled.";
            return;
        }

        _activeMotorCommand = null;
        _pumpRequested = false;
        _autonomousRequested = true;
        Message = "Sending autonomous start command...";
        NotifyControlStateChanged();
        await SendCommandAsync(RobotControlCommand.StartAutonomous, CancellationToken.None);
        if (Message == "Sending autonomous start command...")
        {
            Message = "Autonomous command sent. Confirm movement and sensor response in a supervised test.";
        }
        OnPropertyChanged(nameof(AutonomousStatusTitle));
        OnPropertyChanged(nameof(AutonomousStatusDescription));
        OnPropertyChanged(nameof(ModeSummary));
    }

    private async Task SetMotorCommandAsync(RobotControlCommand command)
    {
        if (!CanControl)
        {
            Message = "Connect to A-Rigator before sending controls.";
            return;
        }

        _autonomousRequested = false;
        _activeMotorCommand = command;
        Message = command == RobotControlCommand.Forward
            ? "Forward command active. Press STOP to halt."
            : "Reverse command active. Press STOP to halt.";
        NotifyControlStateChanged();
        await SendCommandAsync(command, CancellationToken.None);
    }

    private async Task SetPumpAsync(bool enable)
    {
        if (enable && !CanControl)
        {
            Message = "Connect to A-Rigator before turning the pump on.";
            return;
        }

        _autonomousRequested = false;
        _pumpRequested = enable;
        Message = enable ? "Pump command active. Press PUMP OFF to stop it." : "Pump off command sent.";
        NotifyControlStateChanged();
        await SendCommandAsync(enable ? RobotControlCommand.PumpOn : RobotControlCommand.PumpOff, CancellationToken.None);
    }

    private async Task StopAsync()
    {
        _activeMotorCommand = null;
        _pumpRequested = false;
        _autonomousRequested = false;
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
        _autonomousRequested = false;
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
        using var timer = new PeriodicTimer(HeartbeatInterval);

        try
        {
            await RefreshStatusAsync(cancellationToken);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!IsConnected)
                {
                    continue;
                }

                var manualIntentActive = !_autonomousRequested && (_activeMotorCommand is not null || _pumpRequested);
                if (IsConnected && manualIntentActive && _latestStatus?.ControlsEnabled != false)
                {
                    await SendCommandAsync(RobotControlCommand.Heartbeat, cancellationToken);
                }

                if (++statusCounter >= 4)
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
            Message = "Connection lost. The ESP32 manual-control timeout will stop the motor and pump.";
            NotifyControlStateChanged();
        }
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            _latestStatus = await _communicationService.GetStatusAsync(cancellationToken);
            OnPropertyChanged(nameof(RobotStatus));
            OnPropertyChanged(nameof(ModeStatus));
            OnPropertyChanged(nameof(PumpStatus));
            _lastUpdated = DateTimeOffset.Now;
            OnPropertyChanged(nameof(LastUpdatedText));
            OnPropertyChanged(nameof(ModeSummary));
            OnPropertyChanged(nameof(AutonomousStatusTitle));
            OnPropertyChanged(nameof(AutonomousStatusDescription));
            NotifyControlStateChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            NotifyControlStateChanged();
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
                await _communicationService.SendCommandAsync(command, cancellationToken);
            }
            finally
            {
                _commandLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            Message = command == RobotControlCommand.StartAutonomous
                ? "Autonomous start is not available over Wi-Fi in the current firmware. The sketch currently switches modes via USB serial; the matching HTTP endpoint must be added before this app control can activate it."
                : "Command failed or connection was lost. The ESP32 timeout should stop manual outputs.";
            _activeMotorCommand = null;
            _pumpRequested = false;
            _autonomousRequested = false;
            NotifyControlStateChanged();
        }
    }

    private async Task StopOutputsAfterMonitoringAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync();
            await SendCommandAsync(RobotControlCommand.Stop, CancellationToken.None);
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
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(CanControl));
            OnPropertyChanged(nameof(IsControlWarningVisible));
            OnPropertyChanged(nameof(ControlAccessMessage));
            NotifyControlStateChanged();
        });
    }

    private void NotifyControlStateChanged()
    {
        OnPropertyChanged(nameof(MotorStatus));
        OnPropertyChanged(nameof(ModeSummary));
        OnPropertyChanged(nameof(AutonomousStatusTitle));
        OnPropertyChanged(nameof(AutonomousStatusDescription));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanControl));
        OnPropertyChanged(nameof(IsControlWarningVisible));
        OnPropertyChanged(nameof(ControlAccessMessage));
        (ForwardCommand as Command)?.ChangeCanExecute();
        (ReverseCommand as Command)?.ChangeCanExecute();
        (StopCommand as Command)?.ChangeCanExecute();
        (PumpOnCommand as Command)?.ChangeCanExecute();
        (PumpOffCommand as Command)?.ChangeCanExecute();
        (ResetShieldCommand as Command)?.ChangeCanExecute();
        (RefreshCommand as Command)?.ChangeCanExecute();
        (StartAutonomousCommand as Command)?.ChangeCanExecute();
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
