namespace A_Rigator_Controller.Models;

public sealed record RobotProvisioningDevice(
    string DeviceId,
    string Name,
    int SignalStrength,
    string? HardwareId = null);

public sealed record RobotWifiNetwork(
    string Ssid,
    int SignalStrength,
    bool IsSecured);

public sealed record RobotProvisioningResult(
    bool Success,
    string Message,
    string? DeviceAddress = null);

public enum RobotProvisioningState
{
    Idle,
    ScanningForRobots,
    ConnectingToRobot,
    ScanningForNetworks,
    SendingCredentials,
    WaitingForRobotConnection,
    Completed,
    Failed
}
