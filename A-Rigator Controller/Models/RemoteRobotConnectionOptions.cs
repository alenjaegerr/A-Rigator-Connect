namespace A_Rigator_Controller.Models;

/// <summary>Shared MQTT broker connection settings for the single A-Rigator demo robot.</summary>
public sealed record RemoteRobotConnectionOptions(
    string BrokerHost,
    int BrokerPort,
    string Username,
    string Password)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(BrokerHost) &&
        BrokerPort is > 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);
}
