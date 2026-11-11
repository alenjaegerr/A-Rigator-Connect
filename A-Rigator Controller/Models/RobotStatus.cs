namespace A_Rigator_Controller.Models;

public sealed class RobotStatus
{
    public RobotState State { get; init; } = RobotState.Idle;

    public OperatingMode? Mode { get; init; }

    public string? AutonomousMode { get; init; }

    public bool PumpIsOn { get; init; }

    public bool ControlsEnabled { get; init; }

    public bool? WifiConnected { get; init; }

    public string? WifiIp { get; init; }

    public string? Hostname { get; init; }

    public RobotConnectionMode ConnectionMode { get; init; } = RobotConnectionMode.Unknown;

    public float? ObstacleDistanceCm { get; init; }

    public bool? ObstacleEchoDetected { get; init; }

    public bool? ObstacleDetected { get; init; }

    public int? ObstacleStopThresholdCm { get; init; }

    public uint? ManualControlTimeoutMs { get; init; }

    public bool? LineGpio14 { get; init; }

    public bool? LineGpio27 { get; init; }
}
