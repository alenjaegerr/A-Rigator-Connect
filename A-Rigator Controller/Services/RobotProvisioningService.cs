using A_Rigator_Controller.Models;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Microsoft.Maui.ApplicationModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A_Rigator_Controller.Services;

/// <summary>
/// BLE transport for A-Rigator discovery, Wi-Fi scanning, and Wi-Fi credential
/// provisioning over a bonded encrypted GATT characteristic. The BLE Just Works
/// pairing method does not provide MITM protection.
/// </summary>
public sealed class RobotProvisioningService : IRobotProvisioningService, IRobotTransport
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
    public static readonly Guid WifiConfigCharacteristicUuid =
        Guid.Parse("7a1c0006-8f2d-4b6a-9c31-4a5249474154");

    private readonly Dictionary<string, IDevice> _discoveredDevices = new();
    private readonly SemaphoreSlim _gattLock = new(1, 1);
    private IDevice? _connectedDevice;
    private RobotProvisioningState _state = RobotProvisioningState.Idle;

    public RobotProvisioningState State => _state;

    public RobotTransportKind TransportKind => RobotTransportKind.Local;
    private ConnectionState _transportConnectionState = ConnectionState.Disconnected;
    public ConnectionState ConnectionState => _transportConnectionState;
    public event Action<ConnectionState>? ConnectionStateChanged;

    public Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A previously paired device remains a reconnect candidate even when
        // it temporarily leaves range. An explicit DisconnectAsync clears it.
        return Task.FromResult(_connectedDevice is not null);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_connectedDevice is null)
            throw new InvalidOperationException("Discover and pair with A-Rigator over Bluetooth before using local control.");

        if (_connectedDevice.State == DeviceState.Connected)
        {
            SetTransportState(ConnectionState.Connected);
            return;
        }

        SetTransportState(ConnectionState.Reconnecting);
        var rememberedDevice = _connectedDevice;
        var rememberedId = rememberedDevice.Id;

        try
        {
            // First try the previously paired device handle. This is the fastest
            // path when Android still has a valid GATT device reference.
            await _adapter.ConnectToDeviceAsync(
                rememberedDevice,
                cancellationToken: cancellationToken);
            _connectedDevice = rememberedDevice;
            SetTransportState(ConnectionState.Connected);
            Debug.WriteLine("[BLE] Reconnected using the remembered device handle.");
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BLE] Cached-device reconnect failed; rediscovering A-Rigator: {ex}");
        }

        // Android can invalidate a GATT handle after a link drop. Rediscover the
        // same BLE identity instead of permanently retrying a stale IDevice.
        IDevice? rediscoveredDevice = null;
        void OnDeviceDiscovered(object? sender, DeviceEventArgs args)
        {
            if (args.Device.Id == rememberedId)
                rediscoveredDevice = args.Device;
        }

        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        try
        {
            if (_adapter.IsScanning)
                await _adapter.StopScanningForDevicesAsync();

            _adapter.ScanTimeout = 5000;
            await _adapter.StartScanningForDevicesAsync(cancellationToken: cancellationToken);
        }
        finally
        {
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
            if (_adapter.IsScanning)
                await _adapter.StopScanningForDevicesAsync();
        }

        if (rediscoveredDevice is null)
        {
            SetTransportState(ConnectionState.Reconnecting);
            throw new InvalidOperationException(
                "A-Rigator is not currently advertising nearby. The app will keep trying automatically.");
        }

        try
        {
            await _adapter.ConnectToDeviceAsync(
                rediscoveredDevice,
                cancellationToken: cancellationToken);
            _connectedDevice = rediscoveredDevice;
            _discoveredDevices[rediscoveredDevice.Id.ToString("D")] = rediscoveredDevice;
            SetTransportState(ConnectionState.Connected);
            Debug.WriteLine("[BLE] Reconnected after rediscovering A-Rigator.");
        }
        catch
        {
            SetTransportState(ConnectionState.Reconnecting);
            throw;
        }
    }

    public async Task<RobotStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_connectedDevice is null || _connectedDevice.State != DeviceState.Connected)
        {
            SetTransportState(ConnectionState.Disconnected);
            return null;
        }

        await _gattLock.WaitAsync(cancellationToken);
        try
        {
            var service = await _connectedDevice.GetServiceAsync(RobotBleProtocol.ServiceUuid, cancellationToken);
            if (service is null)
                throw new InvalidOperationException(
                    "The connected A-Rigator does not expose the expected BLE robot service. Verify the ESP32 firmware BLE service UUID and reconnect.");

            var characteristic = await service.GetCharacteristicAsync(
                RobotBleProtocol.TelemetryCharacteristicUuid, cancellationToken);
            if (characteristic is null)
                throw new InvalidOperationException(
                    "The connected A-Rigator BLE service does not expose robot telemetry. Verify the ESP32 firmware telemetry characteristic UUID and reconnect.");
            var result = await characteristic.ReadAsync(cancellationToken);
            var json = Encoding.UTF8.GetString(result.data);
            var statusOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            statusOptions.Converters.Add(new JsonStringEnumConverter());
            var status = JsonSerializer.Deserialize<RobotStatus>(json, statusOptions);
            // operatingMode and connectionState are authoritative ESP32 telemetry.
            SetTransportState(ConnectionState.Connected);
            return status;
        }
        catch
        {
            SetTransportState(_connectedDevice?.State == DeviceState.Connected
                ? ConnectionState.Connected
                : ConnectionState.Disconnected);
            throw;
        }
        finally
        {
            _gattLock.Release();
        }
    }

    public async Task SendCommandAsync(RobotControlCommand command, CancellationToken cancellationToken = default)
    {
        // Heartbeats are safety-critical keepalives. The firmware executes them
        // as soon as COMMIT is received, so waiting for a separate GATT
        // acknowledgement only blocks the next heartbeat behind the GATT lock.
        var isHeartbeat = command == RobotControlCommand.Heartbeat;
        await SendBleEnvelopeAsync(
            RobotBleProtocol.SerializeCommand(Guid.NewGuid().ToString("N"), command),
            cancellationToken,
            waitForAcknowledgement: !isHeartbeat);
    }

    public async Task ApplyIrrigationProfileAsync(IrrigationProfile profile, CancellationToken cancellationToken = default)
    {
        await SendBleEnvelopeAsync(RobotBleProtocol.SerializeProfile(profile), cancellationToken);
    }

    public async Task SetAudioVolumeAsync(int volume, CancellationToken cancellationToken = default)
    {
        if (volume is < 0 or > 30) throw new ArgumentOutOfRangeException(nameof(volume));
        await SendBleEnvelopeAsync(RobotBleProtocol.SerializeAudioVolume(volume), cancellationToken);
    }

    public async Task ApplyHardwareConfigurationAsync(RobotHardwareConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        var envelope = JsonSerializer.Serialize(new
        {
            version = RobotBleProtocol.Version,
            commandId = Guid.NewGuid().ToString("N"),
            command = "robot_config",
            issuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            payload = new
            {
                steeringLeft = configuration.SteeringLeftAngle, steeringCenter = configuration.SteeringCenterAngle,
                steeringRight = configuration.SteeringRightAngle, nozzleMinimum = configuration.NozzleMinimumAngle,
                nozzleCenter = configuration.NozzleCenterAngle, nozzleMaximum = configuration.NozzleMaximumAngle,
                nozzleLeftSectorMaximum = configuration.NozzleLeftSectorMaximumAngle,
                nozzleRightSectorMinimum = configuration.NozzleRightSectorMinimumAngle
            }
        });
        await SendBleEnvelopeAsync(envelope, cancellationToken);
    }

    public Task StartManualIrrigationAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(RobotControlCommand.ManualIrrigationStart, cancellationToken);

    public Task StopManualIrrigationAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(RobotControlCommand.ManualIrrigationStop, cancellationToken);

    public void SetRobotAddress(string address)
    {
        // BLE identifies the robot by its connected GATT device, not a LAN address.
    }

    private async Task SendBleEnvelopeAsync(
        string envelope,
        CancellationToken cancellationToken,
        bool waitForAcknowledgement = true)
    {
        if (_connectedDevice is null || _connectedDevice.State != DeviceState.Connected)
        {
            SetTransportState(ConnectionState.Disconnected);
            throw new InvalidOperationException("A-Rigator is not connected over Bluetooth.");
        }

        await _gattLock.WaitAsync(cancellationToken);
        try
        {
            // A GATT operation is not a BLE reconnect. Keep the transport
            // state Connected while commands are being written so the UI does
            // not display its connection overlay for every manual-control press.
            var service = await _connectedDevice.GetServiceAsync(RobotBleProtocol.ServiceUuid, cancellationToken);
            var commandCharacteristic = await service.GetCharacteristicAsync(RobotBleProtocol.CommandCharacteristicUuid, cancellationToken);
            var ackCharacteristic = waitForAcknowledgement
                ? await service.GetCharacteristicAsync(RobotBleProtocol.AcknowledgementCharacteristicUuid, cancellationToken)
                : null;
            if (commandCharacteristic is null || (waitForAcknowledgement && ackCharacteristic is null))
                throw new InvalidOperationException("This firmware does not expose the BLE control protocol. Update the ESP32 firmware.");

            var commandBytes = Encoding.UTF8.GetBytes(envelope);
            if (commandBytes.Length > 240)
                throw new InvalidOperationException("The BLE command payload is too large.");

            await commandCharacteristic.WriteAsync(
                Encoding.ASCII.GetBytes($"BEGIN:{commandBytes.Length}"), cancellationToken);
            for (var offset = 0; offset < commandBytes.Length; offset += 12)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = Encoding.ASCII.GetString(
                    commandBytes, offset, Math.Min(12, commandBytes.Length - offset));
                await commandCharacteristic.WriteAsync(
                    Encoding.ASCII.GetBytes($"D{offset:D3}:{chunk}"), cancellationToken);
            }
            await commandCharacteristic.WriteAsync(Encoding.ASCII.GetBytes("COMMIT"), cancellationToken);

            // The ESP32 processes the command synchronously on COMMIT. A
            // heartbeat needs no user-facing acknowledgement; returning here
            // releases the GATT lock quickly so the next keepalive is not held
            // behind the normal four-second command-acknowledgement window.
            if (!waitForAcknowledgement)
            {
                SetTransportState(ConnectionState.Connected);
                return;
            }

            using var document = JsonDocument.Parse(envelope);
            var commandId = document.RootElement.GetProperty("commandId").GetString();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(4);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(120, cancellationToken);
                var read = await ackCharacteristic!.ReadAsync(cancellationToken);
                using var ackDocument = JsonDocument.Parse(Encoding.UTF8.GetString(read.data));
                var root = ackDocument.RootElement;
                var ackId = root.TryGetProperty("commandId", out var idElement) ? idElement.GetString() : null;
                if (!string.Equals(commandId, ackId, StringComparison.Ordinal))
                    continue;

                var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
                var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
                if (!string.Equals(status, "executed", StringComparison.OrdinalIgnoreCase) &&
                    !(document.RootElement.GetProperty("command").GetString() == "factory_reset" &&
                      string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(message ?? "A-Rigator rejected the Bluetooth command.");
                SetTransportState(ConnectionState.Connected);
                return;
            }

            throw new TimeoutException("A-Rigator did not acknowledge the Bluetooth command.");
        }
        catch
        {
            // A rejected command or malformed telemetry does not mean the
            // BLE link itself has disconnected.
            SetTransportState(_connectedDevice?.State == DeviceState.Connected
                ? ConnectionState.Connected
                : ConnectionState.Disconnected);
            throw;
        }
        finally
        {
            _gattLock.Release();
        }
    }

    private void SetTransportState(ConnectionState state)
    {
        if (_transportConnectionState == state) return;
        _transportConnectionState = state;
        ConnectionStateChanged?.Invoke(state);
    }


    public event Action<RobotProvisioningState>? StateChanged;

    public async Task<IReadOnlyList<RobotProvisioningDevice>> ScanForRobotsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureBluetoothPermissionAsync();

        if (_adapter.IsScanning)
            await _adapter.StopScanningForDevicesAsync();

        SetState(RobotProvisioningState.ScanningForRobots);
        _discoveredDevices.Clear();
        var found = new Dictionary<string, RobotProvisioningDevice>();

        void OnDeviceDiscovered(object? sender, DeviceEventArgs args)
        {
            var device = args.Device;
            var advertisedName = device.Name?.Trim();

            // Temporary diagnostics: capture the event before applying the name filter.
            Debug.WriteLine(
                $"[BLE DISCOVERY] Event received. Name='{advertisedName ?? "<null>"}', " +
                $"Id='{device.Id}', RSSI={device.Rssi}");

            // Do not rely on an Android BLE service-UUID scan filter here.
            // ESP32 may place the 128-bit provisioning UUID in the scan response,
            // which some Android stacks do not match consistently.
            if (string.IsNullOrWhiteSpace(advertisedName) ||
                !advertisedName.Contains("A-Rigator", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("[BLE DISCOVERY] Rejected: device.Name is empty or does not contain 'A-Rigator'.");
                return;
            }

            var id = device.Id.ToString("D");
            _discoveredDevices[id] = device;
            found[id] = new RobotProvisioningDevice(id, advertisedName, device.Rssi);
            Debug.WriteLine($"[BLE DISCOVERY] Accepted A-Rigator. Id='{id}', RSSI={device.Rssi}. Total matches={found.Count}.");
        }

        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        try
        {
            Debug.WriteLine("[BLE DISCOVERY] Starting unfiltered BLE scan. Timeout=12000 ms.");
            _adapter.ScanTimeout = 12000;
            // Scan without a platform service filter, then validate the device name
            // in the discovery callback. This avoids false negatives on scan-response
            // service UUIDs while still keeping unrelated BLE devices out of the UI.
            await _adapter.StartScanningForDevicesAsync(
                cancellationToken: cancellationToken);

            Debug.WriteLine($"[BLE DISCOVERY] Scan completed. Matching A-Rigator devices={found.Count}.");
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

    public async Task<string> GetConnectedRobotHardwareIdAsync(
        CancellationToken cancellationToken = default)
    {
        if (_connectedDevice is null)
            throw new InvalidOperationException("Connect to A-Rigator over Bluetooth first.");

        await _gattLock.WaitAsync(cancellationToken);
        try
        {
            var service = await _connectedDevice.GetServiceAsync(ProvisioningServiceUuid, cancellationToken);
            var characteristic = await service.GetCharacteristicAsync(
                Guid.Parse("7a1c0008-8f2d-4b6a-9c31-4a5249474154"), cancellationToken);
            if (characteristic is null)
                throw new InvalidOperationException("This A-Rigator firmware does not expose its hardware ID. Update the ESP32 sketch and reconnect.");

            var read = await characteristic.ReadAsync(cancellationToken);
            var hardwareId = Encoding.UTF8.GetString(read.data).Trim('\0', ' ', '\r', '\n').ToUpperInvariant();
            if (hardwareId.Length != 12 || !hardwareId.All(Uri.IsHexDigit))
                throw new InvalidOperationException("A-Rigator returned an invalid hardware ID.");

            return hardwareId;
        }
        finally
        {
            _gattLock.Release();
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
                if (response.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"A-Rigator could not scan for nearby Wi-Fi networks ({response}).");
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
                var expectedLength = Math.Min(18, totalLength - offset);
                var chunkText = Encoding.UTF8.GetString(chunk);
                if (chunkText.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"A-Rigator could not return Wi-Fi scan data ({chunkText}).");
                if (chunk.Length != expectedLength)
                    throw new InvalidOperationException(
                        $"A-Rigator returned an incomplete Wi-Fi scan result at byte {offset} (expected {expectedLength}, received {chunk.Length}).");
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
        if (_connectedDevice is null || _connectedDevice.State != DeviceState.Connected)
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
                    SetState(RobotProvisioningState.Completed);
                    return new RobotProvisioningResult(true,
                        "A-Rigator joined the router Wi-Fi network. Bluetooth remains available for local control.");
                }
                if (response.StartsWith("ERROR:", StringComparison.Ordinal))
                {
                    SetState(RobotProvisioningState.Failed);
                    var message = response switch
                    {
                        "ERROR:CONNECT" => "A-Rigator could not join that network. Check the password and signal, then try again. Bluetooth remains available for local control.",
                        "ERROR:SAVE" => "A-Rigator connected, but could not save the Wi-Fi profile.",
                        _ => $"A-Rigator rejected the Wi-Fi setup ({response})."
                    };
                    return new RobotProvisioningResult(false, message);
                }
            }

            SetState(RobotProvisioningState.Failed);
            return new RobotProvisioningResult(false,
                "A-Rigator did not confirm the connection in time. Keep the robot nearby and retry over Bluetooth.");
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

    public async Task<string?> GetSavedWifiNetworkAsync(CancellationToken cancellationToken = default)
    {
        if (_connectedDevice is null || _connectedDevice.State != DeviceState.Connected)
            throw new InvalidOperationException("Connect to A-Rigator over Bluetooth first.");

        await _gattLock.WaitAsync(cancellationToken);
        try
        {
            var service = await _connectedDevice.GetServiceAsync(ProvisioningServiceUuid, cancellationToken);
            var characteristic = await service.GetCharacteristicAsync(WifiConfigCharacteristicUuid, cancellationToken);
            if (characteristic is null)
                throw new InvalidOperationException("This firmware does not report saved Wi-Fi settings yet. Update the ESP32 firmware.");

            var read = await characteristic.ReadAsync(cancellationToken);
            var response = Encoding.UTF8.GetString(read.data).Trim('\0', ' ', '\r', '\n');
            if (string.Equals(response, "NONE", StringComparison.OrdinalIgnoreCase)) return null;
            if (response.StartsWith("SAVED:", StringComparison.Ordinal)) return response.Substring(6);
            throw new InvalidOperationException("A-Rigator returned an unrecognized Wi-Fi status.");
        }
        finally
        {
            _gattLock.Release();
        }
    }

    public async Task<RobotProvisioningResult> ForgetSavedWifiAsync(
        CancellationToken cancellationToken = default)
    {
        if (_connectedDevice is null || _connectedDevice.State != DeviceState.Connected)
            return new RobotProvisioningResult(false, "Connect to A-Rigator over Bluetooth first.");

        await _gattLock.WaitAsync(cancellationToken);
        try
        {
            var service = await _connectedDevice.GetServiceAsync(ProvisioningServiceUuid, cancellationToken);
            var command = await service.GetCharacteristicAsync(WifiCredentialCharacteristicUuid, cancellationToken);
            var status = await service.GetCharacteristicAsync(WifiStatusCharacteristicUuid, cancellationToken);
            if (command is null || status is null)
                return new RobotProvisioningResult(false, "This firmware does not support forgetting saved Wi-Fi yet. Update the ESP32 firmware.");

            await command.WriteAsync(Encoding.UTF8.GetBytes("FORGET"), cancellationToken);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(150, cancellationToken);
                var read = await status.ReadAsync(cancellationToken);
                var response = Encoding.UTF8.GetString(read.data).Trim('\0', ' ', '\r', '\n');
                if (response == "FORGOTTEN")
                    return new RobotProvisioningResult(true, "A-Rigator has forgotten its saved Wi-Fi network. Local Bluetooth control is still available.");
                if (response.StartsWith("ERROR:", StringComparison.Ordinal))
                    return new RobotProvisioningResult(false, response == "ERROR:BUSY"
                        ? "A-Rigator is finishing another Wi-Fi operation. Try again in a moment."
                        : "A-Rigator couldn't forget the saved Wi-Fi network. Please try again.");
            }

            return new RobotProvisioningResult(false, "A-Rigator did not confirm the Wi-Fi reset. Keep it nearby and try again.");
        }
        finally
        {
            _gattLock.Release();
        }
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
            SetTransportState(ConnectionState.Disconnected);
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
