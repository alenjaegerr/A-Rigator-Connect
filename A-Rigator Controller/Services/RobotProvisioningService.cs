using A_Rigator_Controller.Models;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Microsoft.Maui.ApplicationModel;
using System.Text;
using System.Text.Json;

namespace A_Rigator_Controller.Services;

/// <summary>
/// First-stage BLE transport: discovers A-Rigator and establishes a GATT connection.
/// Wi-Fi provisioning operations remain deliberately unavailable until the firmware
/// implements an authenticated write protocol and acknowledgements.
/// </summary>
public sealed class RobotProvisioningService : IRobotProvisioningService
{
    public static readonly Guid ProvisioningServiceUuid =
        Guid.Parse("7a1c0001-8f2d-4b6a-9c31-4a5249474154");

    private readonly IAdapter _adapter = CrossBluetoothLE.Current.Adapter;
    public static readonly Guid WifiScanCharacteristicUuid =
        Guid.Parse("7a1c0003-8f2d-4b6a-9c31-4a5249474154");

    private readonly Dictionary<string, IDevice> _discoveredDevices = new();
    private readonly SemaphoreSlim _gattLock = new(1, 1);
    private IDevice? _connectedDevice;
    private RobotProvisioningState _state = RobotProvisioningState.Idle;

    public RobotProvisioningState State => _state;

    public event Action<RobotProvisioningState>? StateChanged;

    public async Task<IReadOnlyList<RobotProvisioningDevice>> ScanForRobotsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureBluetoothPermissionAsync();

        SetState(RobotProvisioningState.ScanningForRobots);
        _discoveredDevices.Clear();
        var found = new Dictionary<string, RobotProvisioningDevice>();

        void OnDeviceDiscovered(object? sender, DeviceEventArgs args)
        {
            var device = args.Device;
            var id = device.Id.ToString("D");
            _discoveredDevices[id] = device;
            found[id] = new RobotProvisioningDevice(
                id,
                string.IsNullOrWhiteSpace(device.Name) ? "A-Rigator" : device.Name,
                device.Rssi);
        }

        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        try
        {
            _adapter.ScanTimeout = 10000;
            await _adapter.StartScanningForDevicesAsync(
                new ScanFilterOptions { ServiceUuids = new[] { ProvisioningServiceUuid } },
                cancellationToken: cancellationToken);

            SetState(RobotProvisioningState.Idle);
            return found.Values.OrderByDescending(device => device.SignalStrength).ToArray();
        }
        catch
        {
            SetState(RobotProvisioningState.Failed);
            throw;
        }
        finally
        {
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
            if (_adapter.IsScanning)
                await _adapter.StopScanningForDevicesAsync();
        }
    }

    public async Task ConnectToRobotAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        await EnsureBluetoothPermissionAsync();

        if (!_discoveredDevices.TryGetValue(deviceId, out var device))
            throw new ArgumentException("The selected A-Rigator is no longer in the discovery list. Scan again.", nameof(deviceId));

        SetState(RobotProvisioningState.ConnectingToRobot);
        try
        {
            await _adapter.ConnectToDeviceAsync(device, cancellationToken: cancellationToken);
            _connectedDevice = device;
            SetState(RobotProvisioningState.Completed);
        }
        catch
        {
            _connectedDevice = null;
            SetState(RobotProvisioningState.Failed);
            throw;
        }
    }

    public async Task<IReadOnlyList<RobotWifiNetwork>> ScanWifiNetworksAsync(
        CancellationToken cancellationToken = default)
    {
        if (_connectedDevice is null)
            throw new InvalidOperationException("Connect to A-Rigator over Bluetooth before scanning for Wi-Fi.");

        await _gattLock.WaitAsync(cancellationToken);
        try
        {
            SetState(RobotProvisioningState.ScanningForNetworks);
            var service = await _connectedDevice.GetServiceAsync(ProvisioningServiceUuid);
            var characteristic = await service.GetCharacteristicAsync(WifiScanCharacteristicUuid);
            if (characteristic is null)
                throw new InvalidOperationException("This A-Rigator firmware does not support BLE Wi-Fi scanning yet.");

            await characteristic.WriteAsync(Encoding.UTF8.GetBytes("SCAN"));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            var totalLength = -1;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(400, cancellationToken);
                var response = Encoding.UTF8.GetString(await characteristic.ReadAsync());
                if (response == "ERROR")
                    throw new InvalidOperationException("A-Rigator could not scan for nearby Wi-Fi networks.");
                if (response.StartsWith("DONE:", StringComparison.Ordinal) &&
                    int.TryParse(response.AsSpan(5), out totalLength) && totalLength >= 0)
                    break;
            }

            if (totalLength < 0)
                throw new TimeoutException("A-Rigator did not finish scanning for Wi-Fi networks in time.");

            var json = new StringBuilder(totalLength);
            for (var offset = 0; offset < totalLength; offset += 18)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await characteristic.WriteAsync(Encoding.UTF8.GetBytes($"GET:{offset}"));
                var chunk = Encoding.UTF8.GetString(await characteristic.ReadAsync());
                if (string.IsNullOrEmpty(chunk))
                    throw new InvalidOperationException("A-Rigator returned an incomplete Wi-Fi scan result.");
                json.Append(chunk);
            }

            using var document = JsonDocument.Parse(json.ToString());
            var networks = new List<RobotWifiNetwork>();
            if (document.RootElement.TryGetProperty("networks", out var networkArray) &&
                networkArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var network in networkArray.EnumerateArray())
                {
                    var ssid = network.TryGetProperty("ssid", out var ssidElement)
                        ? ssidElement.GetString()
                        : null;
                    if (string.IsNullOrEmpty(ssid)) continue;

                    var rssi = network.TryGetProperty("rssi", out var rssiElement)
                        ? rssiElement.GetInt32()
                        : -100;
                    var secured = network.TryGetProperty("secured", out var securedElement) &&
                                  securedElement.GetBoolean();
                    networks.Add(new RobotWifiNetwork(ssid, rssi, secured));
                }
            }

            SetState(RobotProvisioningState.Completed);
            return networks
                .GroupBy(network => network.Ssid, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(network => network.SignalStrength).First())
                .OrderByDescending(network => network.SignalStrength)
                .ToArray();
        }
        catch
        {
            SetState(RobotProvisioningState.Failed);
            throw;
        }
        finally
        {
            _gattLock.Release();
        }
    }
    public Task<RobotProvisioningResult> SendWifiCredentialsAsync(
        string ssid,
        string password,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RobotProvisioningResult(
            false,
            "Wi-Fi credential transfer is not enabled yet. The firmware needs an authenticated BLE write protocol first."));
    }

    public Task<RobotProvisioningResult> ForgetSavedWifiAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RobotProvisioningResult(
            false,
            "Remote Wi-Fi reset is not enabled yet. Physical reset-button wiring and the authenticated BLE command are still pending."));
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connectedDevice is null)
        {
            SetState(RobotProvisioningState.Idle);
            return;
        }

        try
        {
            await _adapter.DisconnectDeviceAsync(_connectedDevice);
        }
        finally
        {
            _connectedDevice = null;
            SetState(RobotProvisioningState.Idle);
        }
    }

    private static async Task EnsureBluetoothPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Bluetooth>();

        if (status != PermissionStatus.Granted)
            throw new UnauthorizedAccessException(
                "Bluetooth permission is required to discover and connect to A-Rigator.");
    }

    private void SetState(RobotProvisioningState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }
}
