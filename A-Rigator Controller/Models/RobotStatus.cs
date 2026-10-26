namespace A_Rigator_Controller.Models;

public sealed class RobotStatus
{
    public RobotState State { get; init; } = RobotState.Idle;

    public OperatingMode? Mode { get; init; }

    public bool PumpIsOn { get; init; }

    public bool ControlsEnabled { get; init; }
}
