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
}
