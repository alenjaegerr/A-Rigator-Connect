namespace A_Rigator_Controller.Models;

public sealed class RobotStatus
{
    public RobotState State { get; init; } = RobotState.Idle;

    public OperatingMode? Mode { get; init; }

    public bool PumpIsOn { get; init; }

    public bool ControlsEnabled { get; init; }

    public bool? WifiConnected { get; init; }

    public string? WifiIp { get; init; }

    public string? Hostname { get; init; }

    public RobotConnectionMode ConnectionMode { get; init; } = RobotConnectionMode.Unknown;
}
