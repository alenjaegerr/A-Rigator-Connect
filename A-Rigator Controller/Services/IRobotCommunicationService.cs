using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public interface IRobotCommunicationService
{
    ConnectionState ConnectionState { get; }

    RobotConnectionMode PreferredConnectionMode { get; set; }

    event Action<ConnectionState>? ConnectionStateChanged;

    bool IsRemoteBrokerConnected { get; }

    bool IsRemoteRobotResponsive { get; }

    DateTimeOffset? LastRemoteTelemetryAt { get; }

    event Action? RemoteHealthChanged;

    Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<RobotStatus?> GetStatusAsync(CancellationToken cancellationToken = default);

    Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default);

    Task ApplyIrrigationProfileAsync(IrrigationProfile profile, CancellationToken cancellationToken = default);
    Task ApplyHardwareConfigurationAsync(RobotHardwareConfiguration configuration, CancellationToken cancellationToken = default);

    Task SetAudioVolumeAsync(int volume, CancellationToken cancellationToken = default);

    Task SetLedOutputAsync(bool enabled, CancellationToken cancellationToken = default);

    Task StartManualIrrigationAsync(CancellationToken cancellationToken = default);

    Task StopManualIrrigationAsync(CancellationToken cancellationToken = default);

    void SetRobotAddress(string address);
}
