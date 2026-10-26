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
    private string _message = string.Empty;

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

    public string WaterStatus => "UNAVAILABLE";

    public string BatteryStatus => "UNAVAILABLE";

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
        NotifyControlStateChanged();
        _ = StopOutputsAfterMonitoringAsync(cancellation);
    }

    private async Task SetMotorCommandAsync(RobotControlCommand command)
    {
        if (!CanControl)
        {
            Message = "Connect to A-Rigator before sending controls.";
            return;
        }

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

                var manualIntentActive = _activeMotorCommand is not null || _pumpRequested;
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
            Message = "Command failed or connection was lost. The ESP32 timeout should stop manual outputs.";
            _activeMotorCommand = null;
            _pumpRequested = false;
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
