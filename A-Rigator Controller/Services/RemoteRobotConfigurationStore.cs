using A_Rigator_Controller.Models;
using Microsoft.Maui.Storage;

namespace A_Rigator_Controller.Services;

/// <summary>Stores broker endpoint and robot identity; demo MQTT credentials are temporary source constants.</summary>
public sealed class RemoteRobotConfigurationStore : IRemoteRobotConfigurationStore
{
    private const string HostKey = "arigator_mqtt_host";
    private const string PortKey = "arigator_mqtt_port";
    private const string RobotIdKey = "arigator_mqtt_robot_id";
    // TEMPORARY — STI EXPO DEMONSTRATION ONLY.
    // Remove these hardcoded credentials after STI EXPO and restore secure
    // credential provisioning before any production release.
    private const string DemoMqttUsername = "a-rigator-demo";
    private const string DemoMqttPassword = "12345678";

    public async Task<RemoteRobotConnectionOptions?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var host = await SecureStorage.GetAsync(HostKey);
        var robotId = await SecureStorage.GetAsync(RobotIdKey);
        // TEMPORARY — STI EXPO DEMONSTRATION ONLY.
        // These must match RobotMqttConfig.h. Remove after STI EXPO.
        const string username = DemoMqttUsername;
        const string password = DemoMqttPassword;
        var portText = await SecureStorage.GetAsync(PortKey);
        if (!int.TryParse(portText, out var port)) port = 8883;

        var options = new RemoteRobotConnectionOptions(host ?? "", port, robotId ?? "", username ?? "", password ?? "");
        return options.IsValid ? options : null;
    }

    public async Task SaveAsync(RemoteRobotConnectionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.IsValid) throw new ArgumentException("Remote robot connection details are incomplete.", nameof(options));

        await SecureStorage.SetAsync(HostKey, options.BrokerHost);
        await SecureStorage.SetAsync(PortKey, options.BrokerPort.ToString());
        await SecureStorage.SetAsync(RobotIdKey, options.RobotId);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Remove(HostKey);
        SecureStorage.Remove(PortKey);
        SecureStorage.Remove(RobotIdKey);
        return Task.CompletedTask;
    }
}