using System.Text.Json.Serialization;

namespace A_Rigator_Controller.Models;

public sealed class RobotCommandRequest
{
    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

}
