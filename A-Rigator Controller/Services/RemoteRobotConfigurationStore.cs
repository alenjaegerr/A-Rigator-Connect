using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

/// <summary>Fixed MQTT connection settings for the single A-Rigator demonstration robot.</summary>
public sealed class RemoteRobotConfigurationStore : IRemoteRobotConfigurationStore
{
    private const string BrokerHost = "c1d96f86.ala.asia-southeast1.emqxsl.com";
    private const int BrokerPort = 8883;
    // TEMPORARY — STI EXPO DEMONSTRATION ONLY.
    private const string DemoMqttUsername = "a-rigator-demo";
    private const string DemoMqttPassword = "12345678";

    public Task<RemoteRobotConnectionOptions?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new RemoteRobotConnectionOptions(
            BrokerHost, BrokerPort, DemoMqttUsername, DemoMqttPassword);
        return Task.FromResult<RemoteRobotConnectionOptions?>(options.IsValid ? options : null);
    }

    public Task SaveAsync(RemoteRobotConnectionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.IsValid) throw new ArgumentException("Remote MQTT connection details are invalid.", nameof(options));
        // The single-robot demo uses a fixed broker configuration; there is nothing to persist.
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
