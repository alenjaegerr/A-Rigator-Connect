using System.Text.Json.Serialization;

namespace A_Rigator_Controller.Models;

public sealed class RobotCommandResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
