using System.Text.Json.Serialization;

namespace A_Rigator_Controller.Models;

public sealed class RobotStatusResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("pump")]
    public bool? PumpIsOn { get; init; }

    [JsonPropertyName("controlsEnabled")]
    public bool ControlsEnabled { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("wifiConnected")]
    public bool? WifiConnected { get; init; }

    [JsonPropertyName("wifiIp")]
    public string? WifiIp { get; init; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; init; }

    [JsonPropertyName("obstacleDistanceCm")]
    public float? ObstacleDistanceCm { get; init; }

    [JsonPropertyName("obstacleEchoDetected")]
    public bool? ObstacleEchoDetected { get; init; }

    [JsonPropertyName("obstacleDetected")]
    public bool? ObstacleDetected { get; init; }

    [JsonPropertyName("lineGpio14")]
    public bool? LineGpio14 { get; init; }

    [JsonPropertyName("lineGpio27")]
    public bool? LineGpio27 { get; init; }
}
