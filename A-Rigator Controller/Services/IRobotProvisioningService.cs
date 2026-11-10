using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

/// <summary>
/// Transport-independent contract for BLE robot discovery and Wi-Fi provisioning.
/// BLE discovery, Wi-Fi scanning, and credential transfer use the robot's
/// bonded encrypted BLE connection. Just Works does not provide MITM protection.
/// </summary>
public interface IRobotProvisioningService
{
    RobotProvisioningState State { get; }

    event Action<RobotProvisioningState>? StateChanged;

    Task<IReadOnlyList<RobotProvisioningDevice>> ScanForRobotsAsync(
        CancellationToken cancellationToken = default);

    Task ConnectToRobotAsync(
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RobotWifiNetwork>> ScanWifiNetworksAsync(
        CancellationToken cancellationToken = default);

    Task<RobotProvisioningResult> SendWifiCredentialsAsync(
        string ssid,
        string password,
        CancellationToken cancellationToken = default);

    Task<string> GetAccessPointPasswordAsync(
        CancellationToken cancellationToken = default);

    Task<RobotProvisioningResult> SetAccessPointPasswordAsync(
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<RobotProvisioningResult> ForgetSavedWifiAsync(
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
