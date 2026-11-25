using System.Text.Json;
using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

/// <summary>
/// Versioned wire contract shared by the Android BLE transport and ESP32
/// GATT control service. Keep these UUIDs and JSON field names in sync with
/// Firmware/A-RigatorBot/A-RigatorBot.ino.
/// </summary>
internal static class RobotBleProtocol
{
    public static readonly Guid ServiceUuid =
        Guid.Parse("7a1c0009-8f2d-4b6a-9c31-4a5249474154");
    public static readonly Guid CommandCharacteristicUuid =
        Guid.Parse("7a1c000a-8f2d-4b6a-9c31-4a5249474154");
    public static readonly Guid AcknowledgementCharacteristicUuid =
        Guid.Parse("7a1c000b-8f2d-4b6a-9c31-4a5249474154");
    public static readonly Guid TelemetryCharacteristicUuid =
        Guid.Parse("7a1c000c-8f2d-4b6a-9c31-4a5249474154");

    public const int Version = 1;
    public const int MaximumCommandAgeMs = 5000;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static string SerializeCommand(
        string commandId,
        RobotControlCommand command,
        object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        return JsonSerializer.Serialize(new BleCommandEnvelope
        {
            Version = Version,
            CommandId = commandId,
            Command = ToWireCommand(command),
            IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Payload = payload ?? new { }
        }, JsonOptions);
    }

    public static string ToWireCommand(RobotControlCommand command) => command switch
    {
        RobotControlCommand.Forward => "forward",
        RobotControlCommand.Reverse => "reverse",
        RobotControlCommand.SteerLeft => "steer_left",
        RobotControlCommand.SteerRight => "steer_right",
        RobotControlCommand.SteerCenter => "steer_center",
        RobotControlCommand.NozzleSweep => "nozzle_sweep",
        RobotControlCommand.NozzleSweepLeft => "nozzle_sweep_left",
        RobotControlCommand.NozzleSweepBack => "nozzle_sweep_back",
        RobotControlCommand.NozzleSweepRight => "nozzle_sweep_right",
        RobotControlCommand.NozzleSweepFull => "nozzle_sweep_full",
        RobotControlCommand.NozzleSweepStop => "nozzle_sweep_stop",
        RobotControlCommand.ManualIrrigationStart => "irrigation_start",
        RobotControlCommand.ManualIrrigationStop => "irrigation_stop",
        RobotControlCommand.Stop => "stop",
        RobotControlCommand.PumpOn => "pump_on",
        RobotControlCommand.PumpOff => "pump_off",
        RobotControlCommand.ResetShield => "reset_shield",
        RobotControlCommand.FactoryReset => "factory_reset",
        RobotControlCommand.Heartbeat => "heartbeat",
        RobotControlCommand.AutonomousStart => "autonomous_start",
        RobotControlCommand.AutonomousStop => "autonomous_stop",
        RobotControlCommand.PathwayStart => "pathway_start",
        RobotControlCommand.PathwayStop => "pathway_stop",
        RobotControlCommand.TerrainStart => "terrain_start",
        RobotControlCommand.TerrainBiasLeft => "terrain_bias_left",
        RobotControlCommand.TerrainBiasRight => "terrain_bias_right",
        RobotControlCommand.TerrainBiasCenter => "terrain_bias_center",
        RobotControlCommand.TerrainStop => "terrain_stop",
        RobotControlCommand.ManualTerrainStart => "manual_terrain_start",
        RobotControlCommand.ManualTerrainStop => "manual_terrain_stop",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported BLE command.")
    };

    public static string SerializeProfile(IrrigationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.ApplyDirectionMode();
        return JsonSerializer.Serialize(new
        {
            version = Version,
            commandId = Guid.NewGuid().ToString("N"),
            command = "irrigation_profile",
            issuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            payload = new
            {
                left = profile.LeftDurationSeconds,
                back = profile.BackDurationSeconds,
                right = profile.RightDurationSeconds,
                repeat = profile.RepeatCount
            }
        }, JsonOptions);
    }

    private sealed class BleCommandEnvelope
    {
        public int Version { get; init; }
        public string CommandId { get; init; } = "";
        public string Command { get; init; } = "";
        public long IssuedAt { get; init; }
        public object Payload { get; init; } = new { };
    }
}
