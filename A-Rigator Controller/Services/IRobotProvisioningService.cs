using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

/// <summary>
/// Transport-independent contract for BLE robot discovery and Wi-Fi provisioning.
/// BLE discovery and read-only Wi-Fi scanning are implemented. Credential writes
/// remain disabled until the authenticated encryption protocol is complete.
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

    Task<RobotProvisioningResult> ForgetSavedWifiAsync(
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
