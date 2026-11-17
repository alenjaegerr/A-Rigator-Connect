namespace A_Rigator_Controller.Models;

/// <summary>
/// Per-robot MQTT connection material. Secrets are persisted only through
/// MAUI SecureStorage; never serialize this object to ordinary preferences.
/// </summary>
public sealed record RemoteRobotConnectionOptions(
    string BrokerHost,
    int BrokerPort,
    string RobotId,
    string Username,
    string Password)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(BrokerHost) &&
        BrokerPort is > 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(RobotId) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);
}