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
/// BLE transport for A-Rigator discovery, Wi-Fi scanning, and Wi-Fi credential
/// provisioning over a bonded encrypted GATT characteristic. The BLE Just Works
/// pairing method does not provide MITM protection.
/// </summary>
public sealed class RobotProvisioningService : IRobotProvisioningService
{
    public static readonly Guid ProvisioningServiceUuid =
        Guid.Parse("7a1c0001-8f2d-4b6a-9c31-4a5249474154");

    private readonly IAdapter _adapter = CrossBluetoothLE.Current.Adapter;
    public static readonly Guid WifiScanCharacteristicUuid =
        Guid.Parse("7a1c0003-8f2d-4b6a-9c31-4a5249474154");
    public static readonly Guid WifiCredentialCharacteristicUuid =
        Guid.Parse("7a1c0004-8f2d-4b6a-9c31-4a5249474154");
    public static readonly Guid WifiStatusCharacteristicUuid =
        Guid.Parse("7a1c0005-8f2d-4b6a-9c31-4a5249474154");
    public static readonly Guid ApConfigCharacteristicUuid =
        Guid.Parse("7a1c0006-8f2d-4b6a-9c31-4a5249474154");

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
            var service = await _connectedDevice.GetServiceAsync(ProvisioningServiceUuid, cancellationToken);
            var characteristic = await service.GetCharacteristicAsync(WifiScanCharacteristicUuid, cancellationToken);
            if (characteristic is null)
                throw new InvalidOperationException("This A-Rigator firmware does not support BLE Wi-Fi scanning yet.");

            await characteristic.WriteAsync(Encoding.UTF8.GetBytes("SCAN"), cancellationToken);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            var totalLength = -1;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(400, cancellationToken);
                var readResult = await characteristic.ReadAsync(cancellationToken);
                var response = Encoding.UTF8.GetString(readResult.data);
                if (response == "ERROR")
                    throw new InvalidOperationException("A-Rigator could not scan for nearby Wi-Fi networks.");
                if (response.StartsWith("DONE:", StringComparison.Ordinal) &&
                    int.TryParse(response.AsSpan(5), out totalLength) && totalLength >= 0)
                    break;
            }

            if (totalLength < 0)
                throw new TimeoutException("A-Rigator did not finish scanning for Wi-Fi networks in time.");

            using var payloadStream = new MemoryStream(totalLength);
            for (var offset = 0; offset < totalLength; offset += 18)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await characteristic.WriteAsync(Encoding.UTF8.GetBytes($"GET:{offset}"), cancellationToken);
                var readResult = await characteristic.ReadAsync(cancellationToken);
                var chunk = readResult.data;
                if (chunk.Length == 0)
                    throw new InvalidOperationException("A-Rigator returned an incomplete Wi-Fi scan result.");
                payloadStream.Write(chunk, 0, chunk.Length);
            }

            var json = Encoding.UTF8.GetString(payloadStream.ToArray());
            using var document = JsonDocument.Parse(json);
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
    public async Task<RobotProvisioningResult> SendWifiCredentialsAsync(
        string ssid,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (_connectedDevice is null)
            return new RobotProvisioningResult(false, "Connect to A-Rigator over Bluetooth first.");

        var ssidBytes = Encoding.UTF8.GetBytes(ssid);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        if (ssidBytes.Length is < 1 or > 32)
            return new RobotProvisioningResult(false, "The Wi-Fi network name must be between 1 and 32 UTF-8 bytes.");
        if (passwordBytes.Length > 63 || (passwordBytes.Length > 0 && passwordBytes.Length < 8))
            return new RobotProvisioningResult(false, "Use an empty password for an open network, or 8–63 UTF-8 bytes for a secured network.");

        var payload = new byte[2 + ssidBytes.Length + passwordBytes.Length];
        payload[0] = (byte)ssidBytes.Length;
        payload[1] = (byte)passwordBytes.Length;
        Buffer.BlockCopy(ssidBytes, 0, payload, 2, ssidBytes.Length);
        Buffer.BlockCopy(passwordBytes, 0, payload, 2 + ssidBytes.Length, passwordBytes.Length);
        var hex = Convert.ToHexString(payload);

        await _gattLock.WaitAsync(cancellationToken);
        try
        {
            SetState(RobotProvisioningState.SendingCredentials);
            var service = await _connectedDevice.GetServiceAsync(ProvisioningServiceUuid, cancellationToken);
            var credential = await service.GetCharacteristicAsync(WifiCredentialCharacteristicUuid, cancellationToken);
            var status = await service.GetCharacteristicAsync(WifiStatusCharacteristicUuid, cancellationToken);
            if (credential is null || status is null)
                return new RobotProvisioningResult(false, "This firmware does not expose Wi-Fi credential provisioning yet.");

            async Task WriteCommand(string command)
            {
                await credential.WriteAsync(Encoding.ASCII.GetBytes(command), cancellationToken);
            }

            await WriteCommand($"BEGIN:{hex.Length}");
            for (var offset = 0; offset < hex.Length; offset += 12)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = hex.Substring(offset, Math.Min(12, hex.Length - offset));
                await WriteCommand($"D{offset:D3}:{chunk}");
            }
            await WriteCommand("COMMIT");

            SetState(RobotProvisioningState.WaitingForRobotConnection);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(35);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(700, cancellationToken);
                var readResult = await status.ReadAsync(cancellationToken);
                var response = Encoding.UTF8.GetString(readResult.data).Trim('\0', ' ', '\r', '\n');
                if (response.StartsWith("OK:", StringComparison.Ordinal))
                {
                    var address = response["OK:".Length..].Trim();
                    SetState(RobotProvisioningState.Completed);
                    return new RobotProvisioningResult(true,
                        $"A-Rigator joined the Wi-Fi network. Local address: {address}",
                        address);
                }
                if (response.StartsWith("ERROR:", StringComparison.Ordinal))
                {
                    SetState(RobotProvisioningState.Failed);
                    var message = response switch
                    {
                        "ERROR:CONNECT" => "A-Rigator could not join that network. Check the password and signal, then try again. Its hotspot remains available.",
                        "ERROR:SAVE" => "A-Rigator connected, but could not save the Wi-Fi profile.",
                        _ => $"A-Rigator rejected the Wi-Fi setup ({response})."
                    };
                    return new RobotProvisioningResult(false, message);
                }
            }

            SetState(RobotProvisioningState.Failed);
            return new RobotProvisioningResult(false,
                "A-Rigator did not confirm the connection in time. Its direct hotspot should still be available.");
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

    public async Task<string> GetAccessPointPasswordAsync(
        CancellationToken cancellationToken = default)
    {
        if (_connectedDevice is null)
            throw new InvalidOperationException("Connect to A-Rigator over Bluetooth first.");

        await _gattLock.WaitAsync(cancellationToken);
        try
        {
            var service = await _connectedDevice.GetServiceAsync(ProvisioningServiceUuid, cancellationToken);
            var characteristic = await service.GetCharacteristicAsync(ApConfigCharacteristicUuid, cancellationToken);
            if (characteristic is null)
                throw new InvalidOperationException("This firmware does not support AP credential recovery yet.");

            await characteristic.WriteAsync(Encoding.UTF8.GetBytes("GET"), cancellationToken);
            var read = await characteristic.ReadAsync(cancellationToken);
            var response = Encoding.UTF8.GetString(read.data).Trim('\0', ' ', '\r', '\n');
            const string prefix = "PASSWORD:";
            if (!response.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"A-Rigator returned an unexpected AP credential response ({response}).");

            var password = response[prefix.Length..];
            if (password.Length < 8 || password.Length > 63)
                throw new InvalidOperationException("A-Rigator returned an invalid AP password.");
            return password;
        }
        finally
        {
            _gattLock.Release();
        }
    }

    public async Task<RobotProvisioningResult> SetAccessPointPasswordAsync(
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (_connectedDevice is null)
            return new RobotProvisioningResult(false, "Connect to A-Rigator over Bluetooth first.");
        if (Encoding.UTF8.GetByteCount(newPassword) is < 8 or > 63)
            return new RobotProvisioningResult(false, "The AP password must be 8–63 UTF-8 bytes.");

        await _gattLock.WaitAsync(cancellationToken);
        try
        {
            var service = await _connectedDevice.GetServiceAsync(ProvisioningServiceUuid, cancellationToken);
            var characteristic = await service.GetCharacteristicAsync(ApConfigCharacteristicUuid, cancellationToken);
            if (characteristic is null)
                return new RobotProvisioningResult(false, "This firmware does not support AP password changes yet.");

            await characteristic.WriteAsync(Encoding.UTF8.GetBytes($"SET:{newPassword}"), cancellationToken);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(300, cancellationToken);
                var read = await characteristic.ReadAsync(cancellationToken);
                var response = Encoding.UTF8.GetString(read.data).Trim('\0', ' ', '\r', '\n');
                if (response == "OK:AP_RESTARTED")
                    return new RobotProvisioningResult(true, "AP password updated. Reconnect to A-Rigator using the new password.");
                if (response.StartsWith("ERROR:", StringComparison.Ordinal))
                    return new RobotProvisioningResult(false, $"A-Rigator could not change its AP password ({response}).");
            }

            return new RobotProvisioningResult(false, "A-Rigator did not confirm the AP restart. Keep the previous password until you reconnect and verify.");
        }
        finally
        {
            _gattLock.Release();
        }
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
