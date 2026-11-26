namespace A_Rigator_Controller.Models;

/// <summary>Hardware calibration values owned and validated by the ESP32.</summary>
public sealed class RobotHardwareConfiguration
{
    public int SteeringLeftAngle { get; set; } = 15;
    public int SteeringCenterAngle { get; set; } = 90;
    public int SteeringRightAngle { get; set; } = 165;
    public int NozzleMinimumAngle { get; set; } = 30;
    public int NozzleCenterAngle { get; set; } = 90;
    public int NozzleMaximumAngle { get; set; } = 150;
    public int NozzleLeftSectorMaximumAngle { get; set; } = 70;
    public int NozzleRightSectorMinimumAngle { get; set; } = 110;

    public void Validate()
    {
        if (SteeringLeftAngle < 0 || SteeringLeftAngle >= SteeringCenterAngle ||
            SteeringCenterAngle >= SteeringRightAngle || SteeringRightAngle > 180)
            throw new ArgumentOutOfRangeException(nameof(SteeringLeftAngle), "Steering must satisfy 0 ≤ left < center < right ≤ 180.");
        if (NozzleMinimumAngle < 0 || NozzleMinimumAngle >= NozzleLeftSectorMaximumAngle ||
            NozzleLeftSectorMaximumAngle > NozzleCenterAngle ||
            NozzleCenterAngle >= NozzleRightSectorMinimumAngle ||
            NozzleRightSectorMinimumAngle >= NozzleMaximumAngle || NozzleMaximumAngle > 180)
            throw new ArgumentOutOfRangeException(nameof(NozzleMinimumAngle), "Nozzle must satisfy 0 ≤ minimum < left sector ≤ center < right sector < maximum ≤ 180.");
    }
}
