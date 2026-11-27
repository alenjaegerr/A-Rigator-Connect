using System.Text.Json.Serialization;

namespace A_Rigator_Controller.Models;

public sealed class RobotStatus
{
    public RobotState State { get; init; } = RobotState.Idle;

    public string? FirmwareVersion { get; init; }

    public OperatingMode? Mode { get; init; }

    public string? AutonomousMode { get; init; }

    public bool PumpIsOn { get; init; }

    public float? BatteryVoltage { get; init; }

    public bool BatteryVoltageValid { get; init; }

    public bool ControlsEnabled { get; init; }

    public bool? WifiConnected { get; init; }

    public int? WifiRssi { get; init; }

    public string? WifiIp { get; init; }

    public string? Hostname { get; init; }

    // These fields are reported by the ESP32. The robot, not the phone's saved
    // preference, is authoritative about its selected operating mode and state.
    [JsonPropertyName("operatingMode")]
    public string? ReportedOperatingMode { get; init; }

    [JsonPropertyName("connectionState")]
    public string? ReportedConnectionState { get; init; }

    [JsonPropertyName("mqttConnected")]
    public bool? MqttConnected { get; init; }

    [JsonIgnore]
    public RobotConnectionMode ConnectionMode => ReportedOperatingMode?.ToUpperInvariant() switch
    {
        "LOCAL" => RobotConnectionMode.BLE,
        "REMOTE" => RobotConnectionMode.IoT,
        _ => RobotConnectionMode.Unknown
    };

    public float? WaterDistanceCm { get; init; }
    public bool? WaterSensorValid { get; init; }
    public float? WaterLevelPercent { get; init; }
    public int? AudioVolume { get; init; }

    public bool? LedOutputEnabled { get; init; }

    public float? ObstacleDistanceCm { get; init; }

    public bool? ObstacleEchoDetected { get; init; }

    public bool? ObstacleDetected { get; init; }

    public int? ObstacleStopThresholdCm { get; init; }

    public uint? ManualControlTimeoutMs { get; init; }

    public bool? LineGpio14 { get; init; }

    public bool? LineGpio27 { get; init; }

    public int? SteeringLeftAngle { get; init; }
    public int? SteeringCenterAngle { get; init; }
    public int? SteeringRightAngle { get; init; }
    public int? NozzleMinimumAngle { get; init; }
    public int? NozzleCenterAngle { get; init; }
    public int? NozzleMaximumAngle { get; init; }
    public int? NozzleLeftSectorMaximumAngle { get; init; }
    public int? NozzleRightSectorMinimumAngle { get; init; }
}
