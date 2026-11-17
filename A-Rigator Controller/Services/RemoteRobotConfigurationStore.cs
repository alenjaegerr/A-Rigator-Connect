using A_Rigator_Controller.Models;
using Microsoft.Maui.Storage;

namespace A_Rigator_Controller.Services;

/// <summary>Stores broker credentials in platform-backed encrypted storage.</summary>
public sealed class RemoteRobotConfigurationStore : IRemoteRobotConfigurationStore
{
    private const string HostKey = "arigator_mqtt_host";
    private const string PortKey = "arigator_mqtt_port";
    private const string RobotIdKey = "arigator_mqtt_robot_id";
    private const string UsernameKey = "arigator_mqtt_username";
    private const string PasswordKey = "arigator_mqtt_password";

    public async Task<RemoteRobotConnectionOptions?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var host = await SecureStorage.GetAsync(HostKey);
        var robotId = await SecureStorage.GetAsync(RobotIdKey);
        var username = await SecureStorage.GetAsync(UsernameKey);
        var password = await SecureStorage.GetAsync(PasswordKey);
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
        await SecureStorage.SetAsync(UsernameKey, options.Username);
        await SecureStorage.SetAsync(PasswordKey, options.Password);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Remove(HostKey);
        SecureStorage.Remove(PortKey);
        SecureStorage.Remove(RobotIdKey);
        SecureStorage.Remove(UsernameKey);
        SecureStorage.Remove(PasswordKey);
        return Task.CompletedTask;
    }
}